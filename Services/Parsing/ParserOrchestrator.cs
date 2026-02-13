using System.Collections.Generic;
using DocumentFormat.OpenXml.Wordprocessing;
using ModelDto;
using WordParserLibrary.Helpers;
using WordParserLibrary.Services.Parsing.Builders;
using Serilog;
using WordParserLibrary;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Orkiestrator parsowania: jednoprzebiegowy przeplyw, ktory
	/// (1) klasyfikuje akapit, (2) buduje encje, (3) aktualizuje kontekst.
	/// Odpowiada za reset stanu podpoziomow oraz wykrywanie nowelizacji.
	/// </summary>
	public sealed class ParserOrchestrator
	{
		private readonly ParagraphClassifier _classifier = new();
		private readonly ArticleBuilder _articleBuilder = new();
		private readonly ParagraphBuilder _paragraphBuilder = new();
		private readonly PointBuilder _pointBuilder = new();
		private readonly LetterBuilder _letterBuilder = new();
		private readonly TiretBuilder _tiretBuilder = new();
		private readonly AmendmentBuilder _amendmentBuilder = new();
		private readonly AmendmentFinalizer _amendmentFinalizer = new();
		private readonly NumberingContinuityValidator _numberingValidator = new();
		private readonly JournalReferenceService _journalReferenceService = new();

		/// <summary>
		/// Przetwarza pojedynczy akapit i aktualizuje stan kontekstu.
		/// Flow:
		/// - klasyfikacja akapitu i aktualizacja stanu nowelizacji,
		/// - pominiecie tresci nowelizacji,
		/// - budowa encji (ART/UST/PKT/LIT/TIR) i walidacja numeracji,
		/// - reset podpoziomow w kontekscie,
		/// - aktualizacja referencji strukturalnej,
		/// - wykrycie celow i triggerow nowelizacji.
		/// </summary>
		public void ProcessParagraph(Paragraph paragraph, ParsingContext context)
		{
			var rawText = paragraph.GetFullText().Trim();
			if (string.IsNullOrEmpty(rawText))
			{
				return;
			}

			var text = rawText.Sanitize().Trim();

			var styleId = paragraph.StyleId();
			var classification = _classifier.Classify(text, styleId);

			// Zapamietaj stan nowelizacji PRZED aktualizacja
			bool wasInsideAmendment = context.InsideAmendment;

			// Sprawdz i aktualizuj stan nowelizacji PRZED przetwarzaniem
			UpdateAmendmentState(context, classification);

			// Jesli wychodzmy z nowelizacji — zbuduj zebraną treść
			if (wasInsideAmendment && !context.InsideAmendment)
			{
				FlushAmendmentCollector(context);
			}

			// Wyjscie z nowelizacji, gdy pojawia sie nieostylowany trigger nowego punktu/ustepu
			if (ShouldExitAmendmentForNewParentLawTrigger(context, classification, text))
			{
				Log.Debug("Zamknieto nowelizacje: wykryto nowy akapit z triggerem bez stylu ustawy matki");
				FlushAmendmentCollector(context);
				context.InsideAmendment = false;
			}

			// Zbieraj akapity nowelizacji (zamiast pomijania)
			if (classification.IsAmendmentContent || context.InsideAmendment)
			{
				CollectAmendmentParagraph(context, text, styleId);
				return;
			}

			if (TryHandleWrapUpCommonPart(context, rawText, styleId))
			{
				return;
			}

			if (classification.Kind == ParagraphKind.Article)
			{
				var result = _articleBuilder.Build(new ArticleBuildInput(context.Subchapter, text));
				_numberingValidator.ValidateArticle(result.Article);
				context.CurrentArticle = result.Article;
				context.CurrentParagraph = result.Paragraph;
				context.CurrentPoint = null;
				context.CurrentLetter = null;
				context.CurrentTiretIndex = 0;

				UpdateStructuralReference(context, result.Article);
				if (result.Paragraph != null)
				{
					if (!result.Paragraph.IsImplicit)
					{
						UpdateStructuralReference(context, result.Paragraph);
					}
					DetectAmendmentTargets(context, result.Paragraph);
				}
				DetectAmendmentTrigger(context, text);
				_journalReferenceService.ParseJournalReferences(result.Article);
				return;
			}

			if (context.CurrentArticle == null)
			{
				return;
			}

			switch (classification.Kind)
			{
				case ParagraphKind.Paragraph:
					context.CurrentParagraph = _paragraphBuilder.Build(new ParagraphBuildInput(context.CurrentArticle, context.CurrentParagraph, text));
					_numberingValidator.ValidateParagraph(context.CurrentParagraph);
					ValidationReporter.AddClassificationWarning(context.CurrentParagraph, classification, "UST");
					context.CurrentPoint = null;
					context.CurrentLetter = null;
					context.CurrentTiretIndex = 0;

					UpdateStructuralReference(context, context.CurrentParagraph);
					DetectAmendmentTargets(context, context.CurrentParagraph);
					DetectAmendmentTrigger(context, text);
					break;
				case ParagraphKind.Point:
					var ensuredParagraph = _paragraphBuilder.EnsureForPoint(context.CurrentArticle, context.CurrentParagraph);
					context.CurrentParagraph = ensuredParagraph.Paragraph;

					// Wiaz intro z segmentem rodzica przed dodaniem pierwszego punktu
					if (context.CurrentParagraph.Points.Count == 0)
						ParsingFactories.AttachIntroCommonPart(context.CurrentParagraph);

					context.CurrentPoint = _pointBuilder.Build(new PointBuildInput(context.CurrentParagraph, context.CurrentArticle, text));
					_numberingValidator.ValidatePoint(context.CurrentPoint);
					ValidationReporter.AddClassificationWarning(context.CurrentPoint, classification, "PKT");
					context.CurrentLetter = null;
					context.CurrentTiretIndex = 0;

					UpdateStructuralReference(context, context.CurrentPoint);
					DetectAmendmentTargets(context, context.CurrentPoint);
					DetectAmendmentTrigger(context, text);
					break;
				case ParagraphKind.Letter:
					var ensuredPoint = _pointBuilder.EnsureForLetter(context.CurrentParagraph, context.CurrentArticle, context.CurrentPoint);
					context.CurrentPoint = ensuredPoint.Point;
					if (ensuredPoint.CreatedImplicit)
					{
						ValidationReporter.AddValidationMessage(context.CurrentPoint, ValidationLevel.Warning,
							"Brak jawnego punktu; utworzono niejawny punkt na podstawie struktury.");
					}

					// Wiaz intro z segmentem rodzica przed dodaniem pierwszej litery
					if (context.CurrentPoint.Letters.Count == 0)
						ParsingFactories.AttachIntroCommonPart(context.CurrentPoint);

					context.CurrentLetter = _letterBuilder.Build(new LetterBuildInput(context.CurrentPoint, context.CurrentParagraph, context.CurrentArticle, text));
					_numberingValidator.ValidateLetter(context.CurrentLetter);
					ValidationReporter.AddClassificationWarning(context.CurrentLetter, classification, "LIT");
					context.CurrentTiretIndex = 0;

					UpdateStructuralReference(context, context.CurrentLetter);
					DetectAmendmentTargets(context, context.CurrentLetter);
					DetectAmendmentTrigger(context, text);
					break;
				case ParagraphKind.Tiret:
					var ensuredPointForTiret = _pointBuilder.EnsureForLetter(context.CurrentParagraph, context.CurrentArticle, context.CurrentPoint);
					context.CurrentPoint = ensuredPointForTiret.Point;
					if (ensuredPointForTiret.CreatedImplicit)
					{
						ValidationReporter.AddValidationMessage(context.CurrentPoint, ValidationLevel.Warning,
							"Brak jawnego punktu; utworzono niejawny punkt na podstawie struktury.");
					}

					var ensuredLetter = _letterBuilder.EnsureForTiret(context.CurrentPoint, context.CurrentParagraph,
						context.CurrentArticle, context.CurrentLetter);
					context.CurrentLetter = ensuredLetter.Letter;
					if (ensuredLetter.CreatedImplicit)
					{
						ValidationReporter.AddValidationMessage(context.CurrentLetter, ValidationLevel.Warning,
							"Brak jawnej litery; utworzono niejawna litere na podstawie struktury.");
					}

					// Wiaz intro z segmentem rodzica przed dodaniem pierwszego tiretu
					if (context.CurrentLetter.Tirets.Count == 0)
						ParsingFactories.AttachIntroCommonPart(context.CurrentLetter);

					context.CurrentTiretIndex++;
					var tiret = _tiretBuilder.Build(new TiretBuildInput(context.CurrentLetter, context.CurrentPoint, context.CurrentParagraph,
						context.CurrentArticle, text, context.CurrentTiretIndex));
					ValidationReporter.AddClassificationWarning(tiret, classification, "TIR");

					UpdateStructuralReference(context, tiret);
					DetectAmendmentTargets(context, tiret);
					DetectAmendmentTrigger(context, text);
					break;
				default:
					break;
			}
		}

		// ============================================================
		// Zbieranie i budowanie nowelizacji
		// ============================================================

		/// <summary>
		/// Zbiera akapit nowelizacji do bufora. Jesli kolektor nie jest jeszcze uruchomiony,
		/// rozpoczyna zbieranie z odpowiednim wlascicielem i celem.
		/// </summary>
		private void CollectAmendmentParagraph(ParsingContext context, string text, string? styleId)
		{
			if (!context.AmendmentCollector.IsCollecting)
			{
				var owner = context.AmendmentOwner ?? GetCurrentAmendmentOwner(context);
				if (owner != null)
				{
					var target = context.DetectedAmendmentTargets.TryGetValue(owner.Guid, out var t) ? t : null;
					context.AmendmentCollector.Begin(owner, target);
				}
			}

			context.AmendmentCollector.AddParagraph(text, styleId);
		}

		/// <summary>
		/// Buduje nowelizacje z zebranych akapitow i deleguje finalizacje
		/// do AmendmentFinalizer (Faza 3). Wywolywane po zamknieciu nowelizacji
		/// (powrot do stylu ustawy matki) lub na koncu dokumentu.
		/// </summary>
		private void FlushAmendmentCollector(ParsingContext context)
		{
			var collector = context.AmendmentCollector;
			if (!collector.IsCollecting || collector.Count == 0)
			{
				collector.Reset();
				context.AmendmentOwner = null;
				return;
			}

			// Faza 2: budowanie treści nowelizacji
			var buildInput = new AmendmentBuildInput(
				collector.Paragraphs,
				collector.Target,
				AmendmentOperationType.Modification);

			var content = _amendmentBuilder.Build(buildInput);

			// Faza 3: finalizacja — detekcja operacji, linkowanie celów/JournalInfo,
			// przypisanie do właściciela, walidacja
			var finalizerInput = new AmendmentFinalizerInput(content, collector, context);
			_amendmentFinalizer.Finalize(finalizerInput);

			collector.Reset();
			context.AmendmentOwner = null;
		}

		/// <summary>
		/// Zwraca najgłębszą encję w bieżącym kontekście, która implementuje IHasAmendments.
		/// Używane do ustalenia właściciela nowelizacji, gdy nie został jawnie ustawiony
		/// przez trigger.
		/// </summary>
		private static BaseEntity? GetCurrentAmendmentOwner(ParsingContext context)
		{
			if (context.CurrentLetter is IHasAmendments) return context.CurrentLetter;
			if (context.CurrentPoint is IHasAmendments) return context.CurrentPoint;
			if (context.CurrentParagraph is IHasAmendments) return context.CurrentParagraph;
			return null;
		}

		/// <summary>
		/// Finalizuje przetwarzanie — wypróżnia bufor nowelizacji jeśli dokument
		/// kończy się w trakcie tresci nowelizacji.
		/// </summary>
		public void Finalize(ParsingContext context)
		{
			if (context.InsideAmendment || context.AmendmentCollector.IsCollecting)
			{
				Log.Debug("Finalizacja: dokument zakończony wewnątrz nowelizacji, wypłukanie bufora");
				FlushAmendmentCollector(context);
				context.InsideAmendment = false;
			}
		}

		// ============================================================
		// Aktualizacja pozycji strukturalnej
		// ============================================================

		/// <summary>
		/// Aktualizuje biezaca pozycje strukturalna w kontekscie na podstawie
		/// numeru zbudowanej encji. Ustawienie poziomu resetuje podrzedne
		/// (np. SetArticle zeruje ust/pkt/lit/tir), aby zachowac spojne ID.
		/// </summary>
		private static void UpdateStructuralReference(ParsingContext context, BaseEntity entity)
		{
			var numberValue = entity.Number?.Value;
			if (string.IsNullOrEmpty(numberValue))
				return;

			var reference = context.CurrentStructuralReference;
			switch (entity.UnitType)
			{
				case UnitType.Article:
					reference.SetArticle(numberValue);
					break;
				case UnitType.Paragraph:
					reference.SetParagraph(numberValue);
					break;
				case UnitType.Point:
					reference.SetPoint(numberValue);
					break;
				case UnitType.Letter:
					reference.SetLetter(numberValue);
					break;
				case UnitType.Tiret:
					reference.SetTiret(numberValue);
					break;
			}
		}

		/// <summary>
		/// Wykrywa cele nowelizacji w tresci encji implementujacej IHasAmendments.
		/// Parsuje wzorce typu "w art. 5", "po ust. 2" itp. i zapisuje
		/// wykryty cel w kontekscie (DetectedAmendmentTargets) dla dalszego mapowania.
		/// Kontekst jest dziedziczony z encji nadrzednych — jesli np. punkt mowi
		/// "w art. 1:", a litera mowi "w ust. 2 pkt 3 otrzymuje brzmienie:",
		/// to cel litery bedzie zawierac pelna sciezke: art. 1 | ust. 2 | pkt 3.
		/// </summary>
		private static void DetectAmendmentTargets(ParsingContext context, BaseEntity entity)
		{
			if (entity is not IHasAmendments)
				return;

			if (string.IsNullOrWhiteSpace(entity.ContentText))
				return;

			// Zacznij od kontekstu rodzica (jesli istnieje wykryty cel w encji nadrzednej)
			var parentRef = FindParentAmendmentTargetReference(context, entity);
			var targetRef = parentRef?.Clone() ?? new StructuralReference();
			context.ReferenceService.UpdateLegalReference(targetRef, entity.ContentText);

			// Sprawdz czy wykryto jakikolwiek cel nowelizacji
			if (targetRef.Article == null && targetRef.Paragraph == null &&
				targetRef.Point == null && targetRef.Letter == null && targetRef.Tiret == null)
			{
				return;
			}

			var amendmentRef = new StructuralAmendmentReference
			{
				Structure = targetRef,
				RawText = entity.ContentText
			};

			context.DetectedAmendmentTargets[entity.Guid] = amendmentRef;

			Log.Debug("Wykryto cel nowelizacji w {UnitType} [{EntityId}]: {AmendmentTarget}",
				entity.UnitType, entity.Id, amendmentRef);
		}

		/// <summary>
		/// Przeszukuje encje nadrzedne w hierarchii (Parent), szukajac wczesniej
		/// wykrytego celu nowelizacji, ktorego kontekst moze byc odziedziczony.
		/// Dzieki temu referencje rozproszone w roznych poziomach (np. "w art. 1:"
		/// w punkcie i "w ust. 2 pkt 3" w literze) sa kumulowane w jednym celu.
		/// </summary>
		private static StructuralReference? FindParentAmendmentTargetReference(ParsingContext context, BaseEntity entity)
		{
			var current = entity.Parent;
			while (current != null)
			{
				if (context.DetectedAmendmentTargets.TryGetValue(current.Guid, out var parentTarget))
					return parentTarget.Structure;
				current = current.Parent;
			}
			return null;
		}

		/// <summary>
		/// Aktualizuje stan kontekstu nowelizacji na podstawie stylu akapitu.
		/// Logika oparta na stylach (nie na cudzyslowach):
		/// - Styl Z/... → zawsze nowelizacja
		/// - Rozpoznany styl ustawy matki (ART/UST/PKT/LIT/TIR) → wyjscie z nowelizacji
		/// - Brak stylu + trigger → wejscie w nowelizacje
		/// - Brak stylu + juz w nowelizacji → pozostaje w nowelizacji
		/// </summary>
		private static void UpdateAmendmentState(ParsingContext context, ClassificationResult classification)
		{
			// 1. Styl Z/... → zawsze nowelizacja
			if (classification.IsAmendmentContent)
			{
				context.InsideAmendment = true;
				context.AmendmentTriggerDetected = false;
				return;
			}

			// 2. Rozpoznany styl ustawy matki → wyjscie z trybu nowelizacji
			if (classification.StyleType != null)
			{
				if (context.InsideAmendment)
				{
					Log.Debug("Zamknieto nowelizacje (styl ustawy matki: {Style})", classification.StyleType);
					context.InsideAmendment = false;
				}
				// Trigger jest czyszczony — ten akapit ma styl ustawy matki,
				// wiec nie jest trescia nowelizacji. Nowy trigger zostanie
				// ustawiony PO przetworzeniu tego akapitu jesli zawiera zwrot.
				context.AmendmentTriggerDetected = false;
				context.AmendmentOwner = null;
				return;
			}

			// 3. Brak rozpoznanego stylu ustawy matki
			if (context.AmendmentTriggerDetected)
			{
				// Po triggerze napotkano akapit bez stylu → to treść nowelizacji
				context.InsideAmendment = true;
				context.AmendmentTriggerDetected = false;
				Log.Debug("Wejscie w nowelizacje po triggerze (brak stylu ustawy matki)");
				return;
			}

			// 4. Brak stylu + juz w nowelizacji → pozostaje w nowelizacji
			// 5. Brak stylu + normalny tryb → przetwarzane normalnie (z fallback warning)
		}

		private static bool TryHandleWrapUpCommonPart(ParsingContext context, string text, string? styleId)
		{
			if (!TryGetWrapUpTarget(styleId, out var targetKind))
				return false;

			if (!IsWrapUpByText(text))
			{
				Log.Warning(
					"WrapUp pominiety: styl={StyleId}, brak polpauzy w tekscie: {Text}",
					styleId,
					text);
				return true;
			}

			BaseEntity? parent = targetKind switch
			{
				AmendmentTargetKind.Point => context.CurrentParagraph,
				AmendmentTargetKind.Letter => context.CurrentPoint,
				AmendmentTargetKind.Tiret => context.CurrentLetter,
				_ => null
			};

			if (parent == null)
			{
				Log.Warning(
					"WrapUp pominiety: brak rodzica dla {TargetKind} (styl={StyleId})",
					targetKind,
					styleId);
				return true;
			}

			var hasListItems = targetKind switch
			{
				AmendmentTargetKind.Point => context.CurrentParagraph?.Points.Count > 0,
				AmendmentTargetKind.Letter => context.CurrentPoint?.Letters.Count > 0,
				AmendmentTargetKind.Tiret => context.CurrentLetter?.Tirets.Count > 0,
				_ => false
			};

			if (!hasListItems)
			{
				Log.Warning(
					"WrapUp pominiety: brak elementow listy dla {TargetKind} (styl={StyleId})",
					targetKind,
					styleId);
				return true;
			}

			var attached = ParsingFactories.AttachWrapUpCommonPart(parent, text);
			if (attached)
			{
				Log.Debug("WrapUp dodany dla {ParentId}", parent.Id);
			}
			else
			{
				Log.Debug("WrapUp pominiety (duplikat lub pusty tekst) dla {ParentId}", parent.Id);
			}

			return true;
		}

		private static bool IsWrapUpByText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;

			var trimmed = text.TrimStart();
			if (trimmed.Length < 2)
				return false;

			var first = trimmed[0];
			if (first != '\u2013' && first != '-')
				return false;

			return char.IsWhiteSpace(trimmed[1]);
		}

		private static bool TryGetWrapUpTarget(string? styleId, out AmendmentTargetKind targetKind)
		{
			targetKind = AmendmentTargetKind.Unknown;
			if (!StyleLibraryMapper.TryGetStyleInfo(styleId, out var info) || info == null)
				return false;

			var name = info.DisplayName;
			if (name.StartsWith("CZ_WSP_PKT –", StringComparison.OrdinalIgnoreCase))
			{
				targetKind = AmendmentTargetKind.Point;
				return true;
			}

			if (name.StartsWith("CZ_WSP_LIT –", StringComparison.OrdinalIgnoreCase))
			{
				targetKind = AmendmentTargetKind.Letter;
				return true;
			}

			if (name.StartsWith("CZ_WSP_TIR –", StringComparison.OrdinalIgnoreCase))
			{
				targetKind = AmendmentTargetKind.Tiret;
				return true;
			}

			return false;
		}

		private static bool ShouldExitAmendmentForNewParentLawTrigger(
			ParsingContext context,
			ClassificationResult classification,
			string text)
		{
			if (!context.InsideAmendment)
				return false;

			if (classification.IsAmendmentContent || classification.StyleType != null)
				return false;

			if (classification.Kind == ParagraphKind.Unknown)
				return false;

			return AmendmentFinalizer.ModificationPattern.IsMatch(text) ||
				AmendmentFinalizer.RepealPattern.IsMatch(text);
		}

		/// <summary>
		/// Sprawdza czy przetworzony akapit zawiera zwrot rozpoczynajacy nowelizacje.
		/// Wywolywane PO przetworzeniu akapitu (po budowaniu encji),
		/// aby kolejny akapit bez stylu zostal oznaczony jako tresc nowelizacji.
		///
		/// Obsluguje dwa scenariusze:
		/// 1. Uchylenie ("uchyla sie") — natychmiastowe utworzenie nowelizacji
		///    bez tresci (Repeal) poprzez AmendmentFinalizer. Uchylenie nie ma
		///    nastepujacych akapitow z trescia nowelizacji.
		/// 2. Zmiana brzmienia / dodanie ("otrzymuje brzmienie:", "w brzmieniu:") —
		///    ustawienie triggera, aby nastepne akapity zostaly zebrane jako tresc.
		/// </summary>
		private void DetectAmendmentTrigger(ParsingContext context, string text)
		{
			// Uchylenie — natychmiastowa nowelizacja bez treści,
			// delegowana do istniejącego AmendmentFinalizer
			if (AmendmentFinalizer.RepealPattern.IsMatch(text))
			{
				var owner = GetCurrentAmendmentOwner(context);
				if (owner == null)
				{
					Log.Warning("Uchylenie: brak właściciela");
					return;
				}

				var collector = context.AmendmentCollector;
				var target = context.DetectedAmendmentTargets.TryGetValue(owner.Guid, out var t) ? t : null;
				collector.Begin(owner, target);

				var content = new AmendmentContent { ObjectType = AmendmentObjectType.None };
				var finalizerInput = new AmendmentFinalizerInput(content, collector, context);
				_amendmentFinalizer.Finalize(finalizerInput);

				collector.Reset();
				return;
			}

			// Zmiana brzmienia / dodanie — trigger dla kolejnych akapitów
			if (AmendmentFinalizer.ModificationPattern.IsMatch(text))
			{
				context.AmendmentTriggerDetected = true;
				context.AmendmentOwner = GetCurrentAmendmentOwner(context);
				Log.Debug("Wykryto zwrot nowelizacyjny (owner={OwnerId}): {Text}",
					context.AmendmentOwner?.Id ?? "brak",
					text.Length > 80 ? text.Substring(0, 80) + "..." : text);
			}
		}
	}
}
