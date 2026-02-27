using DocumentFormat.OpenXml.Wordprocessing;
using WordParserLibrary.Helpers;
using WordParserLibrary.Services.Parsing.Classification;
using Serilog;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Koordynuje jednoprzebiegowe parsowanie dokumentu prawnego.
	/// Deleguje logikę nowelizacji do <see cref="AmendmentStateManager"/>
	/// oraz budowanie encji do <see cref="StructureProcessor"/>.
	/// </summary>
	public sealed class ParserOrchestrator
	{
		private readonly IParagraphClassifier _classifier;
		private readonly AmendmentStateManager _amendmentManager = new();
		private readonly StructureProcessor _structureProcessor = new();

		/// <summary>
		/// Konstruktor domyślny — używa ParagraphClassifier (tryb legacy).
		/// </summary>
		public ParserOrchestrator() : this(null) { }

		/// <summary>
		/// Konstruktor z wstrzykiwaniem klasyfikatora — umożliwia użycie
		/// <see cref="LayeredParagraphClassifier"/> lub mocka w testach.
		/// </summary>
		public ParserOrchestrator(IParagraphClassifier? classifier)
		{
			_classifier = classifier ?? new ParagraphClassifier();
		}

		/// <summary>
		/// Przetwarza pojedynczy akapit i aktualizuje stan kontekstu.
		/// Flow:
		/// 1. Klasyfikacja akapitu.
		/// 2. Aktualizacja stanu nowelizacji (AmendmentStateManager).
		/// 3. Opcjonalne zamknięcie bieżącej nowelizacji.
		/// 4. Zebranie akapitu jako treść nowelizacji lub budowanie encji (StructureProcessor).
		/// 5. Wykrywanie triggerów nowelizacji po zbudowaniu encji.
		/// </summary>
		public void ProcessParagraph(Paragraph paragraph, ParsingContext context)
		{
			var rawText = paragraph.GetFullText().Trim();
			if (string.IsNullOrEmpty(rawText))
				return;

			var text    = rawText.Sanitize().Trim();
			var styleId = paragraph.StyleId();

			var classification = Classify(text, styleId, context);

			UpdateArticleContext(context, classification, text);

			if (HandleAmendmentFlow(context, classification, text, styleId)) return;

			if (_structureProcessor.TryHandleWrapUp(context, rawText, styleId))
				return;

			if (_structureProcessor.Process(context, classification, text, styleId))
				_amendmentManager.DetectTrigger(context, text);
		}

		/// <summary>
		/// Hermetyzuje wybór klasyfikatora (usuwa smell "is LayeredParagraphClassifier" z głównej metody).
		/// </summary>
		private ClassificationResult Classify(string text, string? styleId, ParsingContext context)
		{
			if (_classifier is LayeredParagraphClassifier layered)
				return layered.Classify(new ClassificationInput(text, styleId)
				{
					ArticleContext = context.CurrentArticleTexts,
				});
			return _classifier.Classify(text, styleId);
		}

		/// <summary>
		/// Hermetyzuje aktualizację kontekstu artykułu dla warstwy AI.
		/// </summary>
		private static void UpdateArticleContext(
			ParsingContext context, ClassificationResult classification, string text)
		{
			if (classification.Kind == ParagraphKind.Article)
				context.CurrentArticleTexts.Clear();
			if (!classification.IsAmendmentContent)
				context.CurrentArticleTexts.Add(text);
		}

		/// <summary>
		/// Hermetyzuje cały cykl stanu nowelizacji. Zwraca true jeśli akapit został skonsumowany.
		/// </summary>
		private bool HandleAmendmentFlow(
			ParsingContext context, ClassificationResult classification,
			string text, string? styleId)
		{
			bool wasInsideAmendment = context.InsideAmendment;
			_amendmentManager.UpdateState(context, classification);

			if (wasInsideAmendment && !context.InsideAmendment)
				_amendmentManager.Flush(context);

			if (_amendmentManager.ShouldExitForNewParentLawTrigger(context, classification, text))
			{
				Log.Debug("Zamknieto nowelizacje: wykryto nowy akapit z triggerem bez stylu ustawy matki");
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

		/// <summary>
		/// Finalizuje przetwarzanie — wypróżnia bufor nowelizacji jeśli dokument
		/// kończy się wewnątrz treści nowelizacji.
		/// </summary>
		public void Finalize(ParsingContext context)
		{
			if (context.InsideAmendment || context.AmendmentCollector.IsCollecting)
			{
				Log.Debug("Finalizacja: dokument zakończony wewnątrz nowelizacji, wypłukanie bufora");
				_amendmentManager.Flush(context);
				context.InsideAmendment = false;
			}
		}
	}
}
