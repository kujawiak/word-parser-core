using DocumentFormat.OpenXml.Wordprocessing;
using WordParserLibrary.Helpers;
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
		private readonly ParagraphClassifier _classifier = new();
		private readonly AmendmentStateManager _amendmentManager = new();
		private readonly StructureProcessor _structureProcessor = new();

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

			var text = rawText.Sanitize().Trim();
			var styleId = paragraph.StyleId();
			var classification = _classifier.Classify(text, styleId);

			// Zapamietaj stan nowelizacji PRZED aktualizacja
			bool wasInsideAmendment = context.InsideAmendment;

			// Sprawdz i aktualizuj stan nowelizacji PRZED przetwarzaniem
			_amendmentManager.UpdateState(context, classification);

			// Jesli wychodzmy z nowelizacji — zbuduj zebraną treść
			if (wasInsideAmendment && !context.InsideAmendment)
				_amendmentManager.Flush(context);

			// Wyjscie z nowelizacji, gdy pojawia sie nieostylowany trigger nowego punktu/ustepu
			if (_amendmentManager.ShouldExitForNewParentLawTrigger(context, classification, text))
			{
				Log.Debug("Zamknieto nowelizacje: wykryto nowy akapit z triggerem bez stylu ustawy matki");
				_amendmentManager.Flush(context);
				context.InsideAmendment = false;
			}

			// Zbieraj akapity nowelizacji (zamiast pomijania)
			if (classification.IsAmendmentContent || context.InsideAmendment)
			{
				_amendmentManager.Collect(context, text, styleId);
				return;
			}

			if (_structureProcessor.TryHandleWrapUp(context, rawText, styleId))
				return;

			// Zbuduj encje domenową i wykryj trigger nowelizacji
			if (_structureProcessor.Process(context, classification, text))
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
				Log.Debug("Finalizacja: dokument zakończony wewnątrz nowelizacji, wypłukanie bufora");
				_amendmentManager.Flush(context);
				context.InsideAmendment = false;
			}
		}
	}
}
