using DocumentFormat.OpenXml.Wordprocessing;
using ModelDto;
using WordParserLibrary.Helpers;
using WordParserLibrary.Services.Parsing.Builders;
using Serilog;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Orkiestrator parsowania: klasyfikuje akapity, deleguje budowanie encji
	/// i aktualizuje kontekst nowelizacji (pozycje strukturalna i wykryte cele).
	/// </summary>
	public sealed class ParserOrchestrator
	{
		private readonly ParagraphClassifier _classifier = new();
		private readonly ArticleBuilder _articleBuilder = new();
		private readonly ParagraphBuilder _paragraphBuilder = new();
		private readonly PointBuilder _pointBuilder = new();
		private readonly LetterBuilder _letterBuilder = new();
		private readonly TiretBuilder _tiretBuilder = new();
		private readonly NumberingContinuityValidator _numberingValidator = new();
		private readonly JournalReferenceService _journalReferenceService = new();

		/// <summary>
		/// Przetwarza pojedynczy akapit i aktualizuje stan kontekstu.
		/// </summary>
		public void ProcessParagraph(Paragraph paragraph, ParsingContext context)
		{
			var text = paragraph.InnerText.Sanitize().Trim();
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			var styleId = paragraph.StyleId();
			var classification = _classifier.Classify(text, styleId);

			// Sprawdz i aktualizuj stan nowelizacji PRZED przetwarzaniem
			UpdateAmendmentState(context, classification);

			// Pomijaj akapity w nowelizacji (styl Z/... lub wewnatrz tresci nowelizacji)
			if (classification.IsAmendmentContent || context.InsideAmendment)
			{
				Log.Debug("Pominieto akapit nowelizacji: styl={StyleId}, insideAmendment={Inside}, text={Text}",
					styleId, context.InsideAmendment, text.Length > 60 ? text.Substring(0, 60) + "..." : text);
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
				if (result.Paragraph != null && !result.Paragraph.IsImplicit)
				{
					UpdateStructuralReference(context, result.Paragraph);
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

		/// <summary>
		/// Aktualizuje biezaca pozycje strukturalna w kontekscie na podstawie
		/// numeru zbudowanej encji. Ustawienie poziomu resetuje podrzedne
		/// (np. SetArticle zeruje ust/pkt/lit/tir).
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
		/// wykryty cel w kontekscie (DetectedAmendmentTargets).
		/// </summary>
		private static void DetectAmendmentTargets(ParsingContext context, BaseEntity entity)
		{
			if (entity is not IHasAmendments)
				return;

			if (string.IsNullOrWhiteSpace(entity.ContentText))
				return;

			var targetRef = new StructuralReference();
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
		/// Aktualizuje stan kontekstu nowelizacji na podstawie stylu akapitu.
		/// Logika oparta na stylach (nie na cudzysłowach):
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

		/// <summary>
		/// Sprawdza czy przetworzony akapit zawiera zwrot rozpoczynajacy nowelizacje.
		/// Wywolywane PO przetworzeniu akapitu (po budowaniu encji).
		/// </summary>
		private static void DetectAmendmentTrigger(ParsingContext context, string text)
		{
			if (text.Contains("otrzymuje brzmienie:", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("w brzmieniu:", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("otrzymują brzmienie:", StringComparison.OrdinalIgnoreCase))
			{
				context.AmendmentTriggerDetected = true;
				Log.Debug("Wykryto zwrot nowelizacyjny: {Text}",
					text.Length > 80 ? text.Substring(0, 80) + "..." : text);
			}
		}
	}
}
