using DocumentFormat.OpenXml.Wordprocessing;
using WordParserLibrary.Helpers;
using WordParserLibrary.Services.Classify;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Koordynuje jednoprzebiegowe parsowanie dokumentu prawnego.
	/// Deleguje logikę nowelizacji do <see cref="AmendmentStateManager"/>
	/// oraz budowanie encji do <see cref="StructureProcessor"/>.
	/// </summary>
	public sealed class ParserOrchestrator
	{
		private readonly IParagraphClassifier    _classifier;
		private readonly AmendmentStateManager   _amendmentManager    = new();
		private readonly StructureProcessor      _structureProcessor  = new();

		/// <summary>
		/// Konstruktor domyślny — używa ParagraphClassifier.
		/// </summary>
		public ParserOrchestrator(IParagraphClassifier? classifier = null)
		{
			_classifier = classifier ?? new ParagraphClassifier();
		}

		/// <summary>
		/// Przetwarza pojedynczy akapit i aktualizuje stan kontekstu.
		/// Flow:
		/// 1. Obliczenie NumberingHint na podstawie bieżącego stanu kontekstu.
		/// 2. Klasyfikacja akapitu (Kind + Confidence).
		/// 3. Aktualizacja stanu nowelizacji (AmendmentStateManager).
		/// 4. Budowanie encji lub dołączanie treści nowelizacji (StructureProcessor).
		/// 5. Wykrywanie triggerów nowelizacji po zbudowaniu encji.
		/// </summary>
		public void ProcessParagraph(Paragraph paragraph, ParsingContext context)
		{
			var rawText = paragraph.GetFullText().Trim();
			if (string.IsNullOrEmpty(rawText))
				return;

			var text    = rawText.Sanitize().Trim();
			var styleId = paragraph.StyleId();

			var hint           = BuildNumberingHint(context);
			var classification = _classifier.Classify(new ClassificationInput(text, styleId)
			{
				NumberingHint = hint,
			});

			if (HandleAmendmentFlow(context, classification, text, styleId)) return;

			if (_structureProcessor.Process(context, classification, text, styleId))
				_amendmentManager.DetectTrigger(context, text);
		}

		/// <summary>
		/// Finalizuje przetwarzanie — wypróżnia bufor nowelizacji jeśli dokument
		/// kończy się wewnątrz treści nowelizacji.
		/// </summary>
		public void Finalize(ParsingContext context)
		{
			if (context.InsideAmendment || context.AmendmentCollector.IsCollecting)
			{
				_amendmentManager.Flush(context);
				context.InsideAmendment = false;
			}
		}

		// ============================================================
		// Obliczanie NumberingHint
		// ============================================================

		/// <summary>
		/// Oblicza podpowiedź numeracyjną na podstawie aktualnego stanu kontekstu.
		/// Zwraca hint dla najbardziej szczegółowego aktywnego poziomu hierarchii.
		/// </summary>
		private static NumberingHint? BuildNumberingHint(ParsingContext context)
		{
			// Poziom: litera (jeśli jest aktywna litera z numerem)
			if (context.CurrentLetter?.Number != null)
				return new NumberingHint
				{
					ExpectedKind   = ParagraphKind.Letter,
					ExpectedNumber = context.CurrentLetter.Number,
				};

			// Poziom: punkt
			if (context.CurrentPoint?.Number != null)
				return new NumberingHint
				{
					ExpectedKind   = ParagraphKind.Point,
					ExpectedNumber = context.CurrentPoint.Number,
				};

			// Poziom: ustęp (pomijamy ustępy niejawne)
			if (context.CurrentParagraph?.Number != null &&
			    context.CurrentParagraph is { IsImplicit: false })
				return new NumberingHint
				{
					ExpectedKind   = ParagraphKind.Paragraph,
					ExpectedNumber = context.CurrentParagraph.Number,
				};

			// Poziom: artykuł
			if (context.CurrentArticle?.Number != null)
				return new NumberingHint
				{
					ExpectedKind   = ParagraphKind.Article,
					ExpectedNumber = context.CurrentArticle.Number,
				};

			return null;
		}

		// ============================================================
		// Obsługa cyklu stanu nowelizacji
		// ============================================================

		/// <summary>
		/// Hermetyzuje cały cykl stanu nowelizacji. Zwraca true jeśli akapit został skonsumowany.
		/// </summary>
		private bool HandleAmendmentFlow(
			ParsingContext       context,
			ClassificationResult classification,
			string               text,
			string?              styleId)
		{
			bool wasInsideAmendment = context.InsideAmendment;
			_amendmentManager.UpdateState(context, classification, text);

			if (wasInsideAmendment && !context.InsideAmendment)
				_amendmentManager.Flush(context);

			if (_amendmentManager.ShouldExitForNewParentLawTrigger(context, classification, text))
			{
				_amendmentManager.Flush(context);
				context.InsideAmendment = false;
			}

			if (classification.IsAmendmentContent || context.InsideAmendment)
			{
				_amendmentManager.Collect(context, text, styleId);
				return true;
			}
			return false;
		}
	}
}
