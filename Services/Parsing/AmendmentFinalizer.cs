using System;
using System.Text.RegularExpressions;
using ModelDto;
using ModelDto.EditorialUnits;
using Serilog;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Dane wejściowe dla finalizatora nowelizacji.
	/// Zawiera wynik budowania (AmendmentContent), kolektor z metadanymi
	/// oraz kontekst parsowania do linkowania z JournalInfo i walidacji.
	/// </summary>
	public sealed record AmendmentFinalizerInput(
		AmendmentContent Content,
		AmendmentCollector Collector,
		ParsingContext Context);

	/// <summary>
	/// Serwis finalizujący nowelizację (Faza 3).
	///
	/// Odpowiada za:
	/// 1. Detekcję typu operacji (Modification/Insertion/Repeal) na podstawie treści triggera
	/// 2. Tworzenie obiektu Amendment z wynikami AmendmentBuilder
	/// 3. Łączenie z JournalInfo (TargetLegalAct z artykułu nadrzędnego)
	/// 4. Przypisanie nowelizacji do encji-właściciela (IHasAmendments)
	/// 5. Walidację i raportowanie diagnostyczne
	///
	/// Wywoływany przez orkiestrator po zamknięciu nowelizacji
	/// (powrót do stylu ustawy matki lub koniec dokumentu).
	/// </summary>
	public sealed class AmendmentFinalizer
	{
		// ============================================================
		// Wzorce tekstowe do rozpoznawania typu operacji
		// ============================================================

		private static readonly Regex InsertionPattern = new(
			@"dodaje\s+się",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static readonly Regex RepealPattern = new(
			@"uchyla\s+się",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static readonly Regex ModificationPattern = new(
			@"(?:otrzymuje\s+brzmienie|otrzymują\s+brzmienie|w\s+brzmieniu|zastępuje\s+się)",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		// ============================================================
		// Główna metoda finalizacji
		// ============================================================

		/// <summary>
		/// Finalizuje nowelizację: tworzy obiekt Amendment, wykrywa typ operacji,
		/// łączy z JournalInfo, przypisuje do właściciela i waliduje wynik.
		/// Zwraca null jeśli brak danych wejściowych (pusty kolektor).
		/// </summary>
		public Amendment? Finalize(AmendmentFinalizerInput input)
		{
			var collector = input.Collector;
			var content = input.Content;
			var context = input.Context;

			if (collector.Owner == null)
			{
				Log.Warning("AmendmentFinalizer: brak właściciela nowelizacji — pomijam finalizację");
				return null;
			}

			// 1. Wykryj typ operacji z treści triggera (ContentText właściciela)
			var operationType = DetectOperationType(collector.Owner.ContentText);

			// 2. Zbuduj obiekt Amendment
			var amendment = new Amendment
			{
				OperationType = operationType,
				Content = operationType == AmendmentOperationType.Repeal ? null : content
			};

			// 3. Połącz z celem nowelizacji (StructuralAmendmentReference)
			LinkTargets(amendment, collector, context);

			// 4. Połącz z JournalInfo (TargetLegalAct)
			var journal = ResolveTargetJournal(collector.Owner, context);
			if (journal != null)
			{
				amendment.TargetLegalAct = journal;
			}

			// 5. Przypisz nowelizację do właściciela
			var assigned = AssignToOwner(amendment, collector.Owner);

			// 6. Walidacja i raportowanie
			ValidateAmendment(amendment, collector.Owner);

			Log.Information(
				"AmendmentFinalizer: {OperationType} przypisana do {UnitType} [{EntityId}] " +
				"(cele: {TargetCount}, treść: {ContentSummary})",
				amendment.OperationType,
				collector.Owner.UnitType,
				collector.Owner.Id,
				amendment.Targets.Count,
				content?.ToString() ?? "brak");

			if (!assigned)
			{
				Log.Warning(
					"AmendmentFinalizer: właściciel {UnitType} [{EntityId}] nie implementuje IHasAmendments — " +
					"nowelizacja utworzona, ale nie przypisana",
					collector.Owner.UnitType,
					collector.Owner.Id);
			}

			return amendment;
		}

		// ============================================================
		// Detekcja typu operacji
		// ============================================================

		/// <summary>
		/// Rozpoznaje typ operacji nowelizacyjnej na podstawie treści triggera.
		/// Priorytet: Repeal > Insertion > Modification (domyślny).
		///
		/// Wzorce:
		/// - "uchyla się", "skreśla się", "traci moc" → Repeal
		/// - "dodaje się" → Insertion
		/// - "otrzymuje brzmienie:", "zastępuje się" → Modification
		/// - Brak wzorca → Modification (domyślny)
		/// </summary>
		internal static AmendmentOperationType DetectOperationType(string? triggerText)
		{
			if (string.IsNullOrWhiteSpace(triggerText))
				return AmendmentOperationType.Modification;

			// Repeal ma najwyższy priorytet — jeśli jest "uchyla się", to uchylenie
			if (RepealPattern.IsMatch(triggerText))
				return AmendmentOperationType.Repeal;

			// Insertion: "dodaje się ... w brzmieniu:" lub samo "dodaje się"
			if (InsertionPattern.IsMatch(triggerText))
				return AmendmentOperationType.Insertion;

			// Modification: "otrzymuje brzmienie:", "zastępuje się"
			if (ModificationPattern.IsMatch(triggerText))
				return AmendmentOperationType.Modification;

			// Domyślnie — Modification
			return AmendmentOperationType.Modification;
		}

		// ============================================================
		// Łączenie z celami nowelizacji
		// ============================================================

		/// <summary>
		/// Łączy nowelizację z wykrytymi celami (StructuralAmendmentReference).
		/// Źródła (w kolejności priorytetu):
		/// 1. Target z kolektora (bezpośrednio z Begin())
		/// 2. DetectedAmendmentTargets z kontekstu (po Guid właściciela)
		/// </summary>
		private static void LinkTargets(Amendment amendment, AmendmentCollector collector, ParsingContext context)
		{
			// Priorytet 1: cel z kolektora
			if (collector.Target != null)
			{
				amendment.Targets.Add(collector.Target);
				return;
			}

			// Priorytet 2: cel z mapy DetectedAmendmentTargets
			if (collector.Owner != null &&
				context.DetectedAmendmentTargets.TryGetValue(collector.Owner.Guid, out var detectedTarget))
			{
				amendment.Targets.Add(detectedTarget);
			}
		}

		// ============================================================
		// Łączenie z JournalInfo
		// ============================================================

		/// <summary>
		/// Rozpoznaje publikator (JournalInfo) aktu zmienianego.
		/// Szuka w hierarchii encji nadrzędnych aż do Article,
		/// która ma sparsowane Journals (z JournalReferenceService).
		/// Fallback: Document.SourceJournal.
		/// </summary>
		internal static JournalInfo? ResolveTargetJournal(BaseEntity owner, ParsingContext context)
		{
			// Szukaj artykułu nadrzędnego z publikatorem
			var article = FindParentArticle(owner);
			if (article is { Journals.Count: > 0 })
			{
				return article.Journals[0];
			}

			// Fallback: publikator z dokumentu (jeśli dokument zawiera jeden akt zmieniany)
			var sourceJournal = context.Document.SourceJournal;
			if (sourceJournal.Year > 0 && sourceJournal.Positions.Count > 0)
			{
				return sourceJournal;
			}

			return null;
		}

		/// <summary>
		/// Nawiguje w górę hierarchii encji, szukając artykułu nadrzędnego.
		/// </summary>
		private static Article? FindParentArticle(BaseEntity? entity)
		{
			var current = entity;
			while (current != null)
			{
				if (current is Article article)
					return article;
				current = current.Article ?? current.Parent;
			}
			return null;
		}

		// ============================================================
		// Przypisanie do właściciela
		// ============================================================

		/// <summary>
		/// Przypisuje nowelizację do encji-właściciela (IHasAmendments).
		/// Zwraca true jeśli przypisanie się powiodło.
		/// </summary>
		private static bool AssignToOwner(Amendment amendment, BaseEntity owner)
		{
			if (owner is IHasAmendments hasAmendments)
			{
				hasAmendments.Amendment = amendment;
				return true;
			}
			return false;
		}

		// ============================================================
		// Walidacja i raportowanie
		// ============================================================

		/// <summary>
		/// Waliduje zbudowaną nowelizację i dodaje komunikaty diagnostyczne
		/// do encji-właściciela.
		/// </summary>
		internal static void ValidateAmendment(Amendment amendment, BaseEntity owner)
		{
			// Brak celów nowelizacji
			if (amendment.Targets.Count == 0)
			{
				ValidationReporter.AddValidationMessage(owner, ValidationLevel.Warning,
					"Nowelizacja bez wykrytego celu strukturalnego (brak referencji art./ust./pkt/lit.).");
			}

			// Repeal nie powinien mieć treści
			if (amendment.OperationType == AmendmentOperationType.Repeal && amendment.Content != null)
			{
				ValidationReporter.AddValidationMessage(owner, ValidationLevel.Info,
					"Uchylenie zawiera treść — może to wskazywać na błędną klasyfikację operacji.");
			}

			// Modification/Insertion powinny mieć treść
			if (amendment.OperationType != AmendmentOperationType.Repeal && amendment.Content == null)
			{
				ValidationReporter.AddValidationMessage(owner, ValidationLevel.Warning,
					$"{amendment.OperationType} bez treści — brak akapitów nowelizacji.");
			}

			// Treść bez żadnych jednostek redakcyjnych (oprócz PlainText)
			if (amendment.Content != null && IsContentEmpty(amendment.Content))
			{
				ValidationReporter.AddValidationMessage(owner, ValidationLevel.Info,
					"Treść nowelizacji nie zawiera jednostek redakcyjnych (tylko tekst/brak treści).");
			}

			// Brak JournalInfo
			if (amendment.TargetLegalAct.Year == 0 || amendment.TargetLegalAct.Positions.Count == 0)
			{
				ValidationReporter.AddValidationMessage(owner, ValidationLevel.Info,
					"Nie wykryto publikatora (Dz.U.) aktu zmienianego dla tej nowelizacji.");
			}
		}

		/// <summary>
		/// Sprawdza czy AmendmentContent jest pusta (nie zawiera żadnych jednostek redakcyjnych).
		/// </summary>
		private static bool IsContentEmpty(AmendmentContent content)
		{
			return content.Articles.Count == 0
				&& content.Paragraphs.Count == 0
				&& content.Points.Count == 0
				&& content.Letters.Count == 0
				&& content.Tirets.Count == 0
				&& content.CommonParts.Count == 0
				&& string.IsNullOrEmpty(content.PlainText);
		}
	}
}
