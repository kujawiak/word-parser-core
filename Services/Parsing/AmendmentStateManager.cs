using ModelDto;
using Serilog;
using WordParserLibrary.Services.Classify;
using WordParserLibrary.Services.Parsing.Builders;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Zarządza cyklem życia nowelizacji w trakcie parsowania:
	/// aktualizuje flagi stanu (InsideAmendment, AmendmentTriggerDetected),
	/// zbiera akapity nowelizacji i finalizuje je do obiektów Amendment.
	/// </summary>
	internal sealed class AmendmentStateManager
	{
		private readonly AmendmentBuilder _amendmentBuilder = new();
		private readonly AmendmentFinalizer _amendmentFinalizer = new();

		/// <summary>
		/// Aktualizuje stan nowelizacji w kontekście na podstawie wyniku klasyfikacji bieżącego akapitu.
		/// Logika oparta na stylach:
		/// - Styl Z/... → zawsze nowelizacja
		/// - Rozpoznany styl ustawy matki (ART/UST/PKT/LIT/TIR) → wyjście z nowelizacji
		/// - Brak stylu + trigger → wejście w nowelizację
		/// - Brak stylu + już w nowelizacji → pozostaje w nowelizacji
		/// </summary>
		public void UpdateState(ParsingContext context, ClassificationResult classification)
		{
			// 1. Styl Z/... → zawsze nowelizacja
			if (classification.IsAmendmentContent)
			{
				context.InsideAmendment = true;
				context.AmendmentTriggerDetected = false;
				return;
			}

			// 2. Rozpoznany styl ustawy matki → wyjście z trybu nowelizacji
			if (classification.StyleType != null)
			{
				if (context.InsideAmendment)
				{
					Log.Debug("Zamknieto nowelizacje (styl ustawy matki: {Style})", classification.StyleType);
					context.InsideAmendment = false;
				}
				// Trigger jest czyszczony — ten akapit ma styl ustawy matki,
				// więc nie jest treścią nowelizacji. Nowy trigger zostanie
				// ustawiony PO przetworzeniu tego akapitu, jeśli zawiera zwrot.
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

			// 4. Brak stylu + już w nowelizacji → pozostaje w nowelizacji
			// 5. Brak stylu + normalny tryb → przetwarzane normalnie (z fallback warning)
		}

		/// <summary>
		/// Zwraca true, gdy podczas nowelizacji pojawia się akapit z triggerem
		/// nowego punktu/ustępu bez stylu ustawy matki — sygnał zamknięcia bieżącej nowelizacji.
		/// </summary>
		public bool ShouldExitForNewParentLawTrigger(
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
		/// Zbiera akapit nowelizacji do bufora. Jeśli kolektor nie jest jeszcze uruchomiony,
		/// rozpoczyna zbieranie z odpowiednim właścicielem i celem.
		/// </summary>
		public void Collect(ParsingContext context, string text, string? styleId)
		{
			if (!context.AmendmentCollector.IsCollecting)
			{
				var owner = context.AmendmentOwner ?? GetOwner(context);
				if (owner != null)
				{
					var target = context.DetectedAmendmentTargets.TryGetValue(owner.Guid, out var t) ? t : null;
					context.AmendmentCollector.Begin(owner, target);
				}
			}

			context.AmendmentCollector.AddParagraph(text, styleId);
		}

		/// <summary>
		/// Buduje nowelizację z zebranych akapitów i deleguje finalizację
		/// do AmendmentFinalizer (Faza 3). Wywołuje się po zamknięciu nowelizacji
		/// (powrót do stylu ustawy matki) lub na końcu dokumentu.
		/// </summary>
		public void Flush(ParsingContext context)
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
		/// Sprawdza czy przetworzony akapit zawiera zwrot rozpoczynający nowelizację.
		/// Wywołuje się PO przetworzeniu akapitu (po budowaniu encji).
		///
		/// Obsługuje dwa scenariusze:
		/// 1. Uchylenie ("uchyla się") — natychmiastowe utworzenie nowelizacji Repeal bez treści.
		/// 2. Zmiana brzmienia / dodanie ("otrzymuje brzmienie:", "w brzmieniu:") —
		///    ustawienie triggera dla kolejnych akapitów.
		/// </summary>
		public void DetectTrigger(ParsingContext context, string text)
		{
			// Uchylenie — natychmiastowa nowelizacja bez treści
			if (AmendmentFinalizer.RepealPattern.IsMatch(text))
			{
				var owner = GetOwner(context);
				if (owner == null)
				{
					Log.Warning("Uchylenie: brak właściciela");
					return;
				}

				var collector = context.AmendmentCollector;

				// Zapobieganie utracie poprzedniej nowelizacji gdy kolektor jest aktywny
				if (collector.IsCollecting && collector.Count > 0)
				{
					Log.Warning("Uchylenie wykryte podczas aktywnego zbierania nowelizacji; " +
						"finalizacja poprzedniej przed uchyleniem (owner={OwnerId})",
						collector.Owner?.Id ?? "brak");
					Flush(context);
				}

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
				context.AmendmentOwner = GetOwner(context);
				Log.Debug("Wykryto zwrot nowelizacyjny (owner={OwnerId}): {Text}",
					context.AmendmentOwner?.Id ?? "brak",
					text.Length > 80 ? text.Substring(0, 80) + "..." : text);
			}
		}

		/// <summary>
		/// Zwraca najgłębszą encję w bieżącym kontekście implementującą IHasAmendments.
		/// Używane do ustalenia właściciela nowelizacji.
		/// </summary>
		public static BaseEntity? GetOwner(ParsingContext context)
		{
			if (context.CurrentLetter is IHasAmendments) return context.CurrentLetter;
			if (context.CurrentPoint is IHasAmendments) return context.CurrentPoint;
			if (context.CurrentParagraph is IHasAmendments) return context.CurrentParagraph;
			return null;
		}
	}
}
