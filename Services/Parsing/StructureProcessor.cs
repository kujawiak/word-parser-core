using ModelDto;
using ModelDto.EditorialUnits;
using Serilog;
using WordParserLibrary.Helpers;
using WordParserLibrary.Services.Classify;
using WordParserLibrary.Services.Parsing.Builders;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Buduje encje domenowe (Article, Paragraph, Point, Letter, Tiret)
	/// na podstawie wyników klasyfikacji akapitu. Odpowiada za:
	/// - budowanie encji i resetowanie podpoziomów w kontekście,
	/// - transfer kar numeracyjnych z ClassificationResult jako ValidationMessage,
	/// - aktualizację pozycji strukturalnej (CurrentStructuralReference),
	/// - wykrywanie celów nowelizacji w treści encji,
	/// - obsługę akapitów wrap-up (CZ_WSP_*).
	/// </summary>
	internal sealed class StructureProcessor
	{
		private readonly ArticleBuilder _articleBuilder = new();
		private readonly ParagraphBuilder _paragraphBuilder = new();
		private readonly PointBuilder _pointBuilder = new();
		private readonly LetterBuilder _letterBuilder = new();
		private readonly TiretBuilder _tiretBuilder = new();
		private readonly JournalReferenceService _journalReferenceService = new();

		/// <summary>
		/// Obsługuje akapit wrap-up (zamknięcie listy przez półpauzę przed pozycją nadrzędną).
		/// Wywoływana z Process() gdy classification.Kind == WrapUp.
		/// </summary>
		private static bool TryHandleWrapUp(ParsingContext context, string text, string? styleId)
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
				AmendmentTargetKind.Point  => context.CurrentParagraph,
				AmendmentTargetKind.Letter => context.CurrentPoint,
				AmendmentTargetKind.Tiret  => context.CurrentLetter,
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
				AmendmentTargetKind.Point  => context.CurrentParagraph?.Points.Count > 0,
				AmendmentTargetKind.Letter => context.CurrentPoint?.Letters.Count > 0,
				AmendmentTargetKind.Tiret  => context.CurrentLetter?.Tirets.Count > 0,
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
			Log.Debug(attached
				? "WrapUp dodany dla {ParentId}"
				: "WrapUp pominiety (duplikat lub pusty tekst) dla {ParentId}",
				parent.Id);

			return true;
		}

		/// <summary>
		/// Przetwarza akapit: buduje encję domenową na podstawie klasyfikacji,
		/// aktualizuje pozycję strukturalną i wykrywa cele nowelizacji.
		/// Zwraca true jeśli encja została zbudowana (wykrywanie triggera powinno nastąpić po powrocie).
		/// </summary>
		public bool Process(ParsingContext context, ClassificationResult classification, string text,
			string? sourceStyleId = null)
		{
			if (classification.Kind == ParagraphKind.Article)
			{
				var result = _articleBuilder.Build(new ArticleBuildInput(context.Subchapter, text));
				ValidationReporter.AddClassificationWarning(result.Article, classification, "ART");
				context.CurrentArticle = result.Article;
				context.CurrentParagraph = result.Paragraph;
				context.CurrentPoint = null;
				context.CurrentLetter = null;
				context.CurrentTiretIndex = 0;

				UpdateStructuralReference(context, result.Article);
				if (result.Paragraph != null)
				{
					if (!result.Paragraph.IsImplicit)
						UpdateStructuralReference(context, result.Paragraph);
					DetectAmendmentTargets(context, result.Paragraph);
				}
				_journalReferenceService.ParseJournalReferences(result.Article);
				return true;
			}

			// Nierozpoznany — inference-first, potem diagnostyka
			if (classification.Kind == ParagraphKind.Unknown)
			{
				HandleUnknown(context, text, sourceStyleId);
				return true; // zawsze "skonsumowany"
			}

			if (context.CurrentArticle == null)
			{
				Log.Warning("Akapit {Kind} poza kontekstem artykułu (metadane lub pre-art.) — pominięty. Styl: {StyleId}",
					classification.Kind, sourceStyleId);
				return false;
			}

			switch (classification.Kind)
			{
				case ParagraphKind.Paragraph:
					context.CurrentParagraph = _paragraphBuilder.Build(
						new ParagraphBuildInput(context.CurrentArticle, context.CurrentParagraph, text));
					ValidationReporter.AddClassificationWarning(context.CurrentParagraph, classification, "UST");
					context.CurrentPoint = null;
					context.CurrentLetter = null;
					context.CurrentTiretIndex = 0;

					UpdateStructuralReference(context, context.CurrentParagraph);
					DetectAmendmentTargets(context, context.CurrentParagraph);
					return true;

				case ParagraphKind.Point:
					var ensuredParagraph = _paragraphBuilder.EnsureForPoint(context.CurrentArticle, context.CurrentParagraph);
					context.CurrentParagraph = ensuredParagraph.Paragraph;

					// Wiąż intro z segmentem rodzica przed dodaniem pierwszego punktu
					if (context.CurrentParagraph.Points.Count == 0)
						ParsingFactories.AttachIntroCommonPart(context.CurrentParagraph);

					context.CurrentPoint = _pointBuilder.Build(
						new PointBuildInput(context.CurrentParagraph, context.CurrentArticle, text));
					ValidationReporter.AddClassificationWarning(context.CurrentPoint, classification, "PKT");
					context.CurrentLetter = null;
					context.CurrentTiretIndex = 0;

					UpdateStructuralReference(context, context.CurrentPoint);
					DetectAmendmentTargets(context, context.CurrentPoint);
					return true;

				case ParagraphKind.Letter:
					var ensuredPoint = _pointBuilder.EnsureForLetter(context.CurrentParagraph, context.CurrentArticle, context.CurrentPoint);
					context.CurrentPoint = ensuredPoint.Point;
					if (ensuredPoint.CreatedImplicit)
					{
						ValidationReporter.AddValidationMessage(context.CurrentPoint, ValidationLevel.Warning,
							"Brak jawnego punktu; utworzono niejawny punkt na podstawie struktury.");
					}

					// Wiąż intro z segmentem rodzica przed dodaniem pierwszej litery
					if (context.CurrentPoint.Letters.Count == 0)
						ParsingFactories.AttachIntroCommonPart(context.CurrentPoint);

					context.CurrentLetter = _letterBuilder.Build(
						new LetterBuildInput(context.CurrentPoint, context.CurrentParagraph, context.CurrentArticle, text));
					ValidationReporter.AddClassificationWarning(context.CurrentLetter, classification, "LIT");
					context.CurrentTiretIndex = 0;

					UpdateStructuralReference(context, context.CurrentLetter);
					DetectAmendmentTargets(context, context.CurrentLetter);
					return true;

				case ParagraphKind.Tiret:
					var ensuredPointForTiret = _pointBuilder.EnsureForLetter(context.CurrentParagraph, context.CurrentArticle, context.CurrentPoint);
					context.CurrentPoint = ensuredPointForTiret.Point;
					if (ensuredPointForTiret.CreatedImplicit)
					{
						ValidationReporter.AddValidationMessage(context.CurrentPoint, ValidationLevel.Warning,
							"Brak jawnego punktu; utworzono niejawny punkt na podstawie struktury.");
					}

					var ensuredLetter = _letterBuilder.EnsureForTiret(
						context.CurrentPoint, context.CurrentParagraph, context.CurrentArticle, context.CurrentLetter);
					context.CurrentLetter = ensuredLetter.Letter;
					if (ensuredLetter.CreatedImplicit)
					{
						ValidationReporter.AddValidationMessage(context.CurrentLetter, ValidationLevel.Warning,
							"Brak jawnej litery; utworzono niejawna litere na podstawie struktury.");
					}

					// Wiąż intro z segmentem rodzica przed dodaniem pierwszego tiretu
					if (context.CurrentLetter.Tirets.Count == 0)
						ParsingFactories.AttachIntroCommonPart(context.CurrentLetter);

					context.CurrentTiretIndex++;
					var tiret = _tiretBuilder.Build(new TiretBuildInput(
						context.CurrentLetter, context.CurrentPoint, context.CurrentParagraph,
						context.CurrentArticle, text, context.CurrentTiretIndex));
					ValidationReporter.AddClassificationWarning(tiret, classification, "TIR");

					UpdateStructuralReference(context, tiret);
					DetectAmendmentTargets(context, tiret);
					return true;

				case ParagraphKind.WrapUp:
					TryHandleWrapUp(context, text, sourceStyleId);
					return true;

				default:
					Log.Warning("Nieobsługiwany ParagraphKind {Kind} w switch — pominięty (styl={StyleId})",
						classification.Kind, sourceStyleId);
					return false;
			}
		}

		// ============================================================
		// Metody pomocnicze
		// ============================================================

		/// <summary>
		/// Obsługuje akapit Unknown: najpierw próbuje wywnioskować rodzaj z treści,
		/// a gdy brak wzorca — dołącza ValidationMessage do najgłębszej aktywnej encji.
		/// </summary>
		private void HandleUnknown(ParsingContext context, string text, string? sourceStyleId)
		{
			// Krok 1: wywnioskuj z treści (ochrona przed edge cases LayeredClassifier)
			if (context.CurrentArticle != null)
			{
				var inferredKind = TryInferKindFromText(text);
				if (inferredKind != ParagraphKind.Unknown)
				{
					Log.Warning("Akapit Unknown — wywnioskowany jako {Kind} z wzorca tekstowego " +
						"(styl: {StyleId}): {Text}",
						inferredKind, sourceStyleId,
						text.Length > 60 ? text[..60] + "…" : text);

					var rescued = new ClassificationResult
					{
						Kind       = inferredKind,
						Confidence = 50,
						Penalties  =
						[
							new ClassificationPenalty
							{
								Reason = "Wywnioskowano z wzorca tekstowego po braku klasyfikacji",
								Value  = 50,
							},
						],
					};
					Process(context, rescued, text, sourceStyleId);
					return;
				}
			}

			// Krok 2: brak wzorca — diagnostyka bez encji
			if (context.CurrentArticle == null)
			{
				Log.Warning("Akapit Unknown przed pierwszym artykułem (metadane) — pominięty. " +
					"Styl: {StyleId}, Tekst: {Text}",
					sourceStyleId, text.Length > 80 ? text[..80] + "…" : text);
				return;
			}

			var deepest = (BaseEntity?)context.CurrentLetter
				?? (BaseEntity?)context.CurrentPoint
				?? (BaseEntity?)context.CurrentParagraph
				?? (BaseEntity?)context.CurrentArticle;

			deepest?.ValidationMessages.Add(new ValidationMessage(
				ValidationLevel.Warning,
				$"Pominięty akapit nierozpoznany (styl: '{sourceStyleId ?? "brak"}')." +
				$" Tekst: '{(text.Length > 80 ? text[..80] + "…" : text)}'"));

			Log.Warning("Akapit Unknown pominięty — brak dopasowania do wzorca. " +
				"Styl: {StyleId}, Tekst: {Text}",
				sourceStyleId, text.Length > 80 ? text[..80] + "…" : text);
		}

		private static ParagraphKind TryInferKindFromText(string text)
		{
			if (ParagraphClassifier.IsParagraphByText(text)) return ParagraphKind.Paragraph;
			if (ParagraphClassifier.IsPointByText(text))     return ParagraphKind.Point;
			if (ParagraphClassifier.IsLetterByText(text))    return ParagraphKind.Letter;
			if (ParagraphClassifier.IsTiretByText(text))     return ParagraphKind.Tiret;
			return ParagraphKind.Unknown;
		}

		/// <summary>
		/// Aktualizuje bieżącą pozycję strukturalną w kontekście na podstawie
		/// numeru zbudowanej encji. Ustawienie poziomu resetuje podrzędne
		/// (np. WithArticle zeruje ust/pkt/lit/tir), aby zachować spójne eId.
		/// </summary>
		private static void UpdateStructuralReference(ParsingContext context, BaseEntity entity)
		{
			var numberValue = entity.Number?.Value;
			if (string.IsNullOrEmpty(numberValue))
				return;

			context.CurrentStructuralReference = entity.UnitType switch
			{
				UnitType.Article   => context.CurrentStructuralReference.WithArticle(numberValue),
				UnitType.Paragraph => context.CurrentStructuralReference.WithParagraph(numberValue),
				UnitType.Point     => context.CurrentStructuralReference.WithPoint(numberValue),
				UnitType.Letter    => context.CurrentStructuralReference.WithLetter(numberValue),
				UnitType.Tiret     => context.CurrentStructuralReference.WithTiret(numberValue),
				_                  => context.CurrentStructuralReference
			};
		}

		/// <summary>
		/// Wykrywa cele nowelizacji w treści encji implementującej IHasAmendments.
		/// Parsuje wzorce takie jak "w art. 5", "po ust. 2" itp.
		/// Kontekst jest dziedziczony z encji nadrzędnych (kumulacja referencji rozproszonych po poziomach).
		/// </summary>
		private static void DetectAmendmentTargets(ParsingContext context, BaseEntity entity)
		{
			if (entity is not IHasAmendments)
				return;

			if (string.IsNullOrWhiteSpace(entity.ContentText))
				return;

			// Zacznij od kontekstu rodzica (jeśli istnieje wykryty cel w encji nadrzędnej)
			var parentRef = FindParentAmendmentTargetReference(context, entity);
			var targetRef = parentRef?.Clone() ?? new StructuralReference();
			context.ReferenceService.UpdateLegalReference(targetRef, entity.ContentText);

			// Sprawdź czy wykryto jakikolwiek cel nowelizacji
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
		/// Przeszukuje encje nadrzędne w hierarchii (Parent), szukając wcześniej
		/// wykrytego celu nowelizacji, którego kontekst może być odziedziczony.
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
			if (name.StartsWith("CZ_WSP_PKT \u2013", StringComparison.OrdinalIgnoreCase))
			{
				targetKind = AmendmentTargetKind.Point;
				return true;
			}

			if (name.StartsWith("CZ_WSP_LIT \u2013", StringComparison.OrdinalIgnoreCase))
			{
				targetKind = AmendmentTargetKind.Letter;
				return true;
			}

			if (name.StartsWith("CZ_WSP_TIR \u2013", StringComparison.OrdinalIgnoreCase))
			{
				targetKind = AmendmentTargetKind.Tiret;
				return true;
			}

			return false;
		}
	}
}
