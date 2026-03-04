using ModelDto;
using Serilog;
using WordParserLibrary.Services.Classify;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Walidator ciągłości numeracji jednostek redakcyjnych.
	/// Sprawdza, czy kolejne encje (artykuły, ustępy, punkty, litery)
	/// mają numery następujące po sobie (NumericPart lub LexicalPart).
	/// 
	/// Reguły:
	/// - Ten sam NumericPart jest dozwolony (np. 2 → 2a, oznacza wariant z sufiksem)
	/// - Kolejny NumericPart (prev + 1) jest dozwolony (np. 2a → 3, 5 → 6)
	/// - Dla liter (NumericPart == 0): sprawdza ciągłość LexicalPart (a → b → c)
	/// - Każdy inny przypadek generuje ValidationMessage z poziomem Warning
	/// 
	/// Stan walidatora resetuje się hierarchicznie: nowy artykuł zeruje
	/// śledzenie ustępów/punktów/liter, nowy ustęp zeruje punkty/litery itd.
	/// </summary>
	public sealed class NumberingContinuityValidator
	{
		private EntityNumber? _lastArticleNumber;
		private EntityNumber? _lastParagraphNumber;
		private EntityNumber? _lastPointNumber;
		private EntityNumber? _lastLetterNumber;

		/// <summary>
		/// Waliduje ciągłość numeracji artykułu i resetuje podrzędne liczniki.
		/// </summary>
		public void ValidateArticle(BaseEntity entity)
		{
			if (entity.Number == null)
				return;

			ValidateNumbering(entity, _lastArticleNumber, "artykułu");
			_lastArticleNumber = entity.Number;

			// Reset podrzędnych - nowy artykuł = nowa sekwencja ustępów/punktów/liter
			_lastParagraphNumber = null;
			_lastPointNumber = null;
			_lastLetterNumber = null;
		}

		/// <summary>
		/// Waliduje ciągłość numeracji ustępu i resetuje podrzędne liczniki.
		/// Pomija ustępy niejawne (IsImplicit).
		/// </summary>
		public void ValidateParagraph(BaseEntity entity)
		{
			if (entity.Number == null)
				return;

			if (entity is ModelDto.EditorialUnits.Paragraph { IsImplicit: true })
				return;

			ValidateNumbering(entity, _lastParagraphNumber, "ustępu");
			_lastParagraphNumber = entity.Number;

			// Reset podrzędnych
			_lastPointNumber = null;
			_lastLetterNumber = null;
		}

		/// <summary>
		/// Waliduje ciągłość numeracji punktu i resetuje podrzędny licznik liter.
		/// </summary>
		public void ValidatePoint(BaseEntity entity)
		{
			if (entity.Number == null)
				return;

			ValidateNumbering(entity, _lastPointNumber, "punktu");
			_lastPointNumber = entity.Number;

			// Reset podrzędnych
			_lastLetterNumber = null;
		}

		/// <summary>
		/// Waliduje ciągłość numeracji litery.
		/// </summary>
		public void ValidateLetter(BaseEntity entity)
		{
			if (entity.Number == null)
				return;

			ValidateLetterNumbering(entity, _lastLetterNumber);
			_lastLetterNumber = entity.Number;
		}

		/// <summary>
		/// Waliduje ciągłość numeracji dla jednostek z NumericPart (art, ust, pkt).
		/// Dozwolone przejścia:
		///   - ten sam NumericPart (np. 2 → 2a — wariant z sufiksem/indeksem górnym)
		///   - NumericPart + 1 (np. 2a → 3, 5 → 6 — kolejny numer)
		/// </summary>
		private static void ValidateNumbering(BaseEntity entity, EntityNumber? previousNumber, string unitLabel)
		{
			if (previousNumber == null)
				return;

			var current = entity.Number!;
			var previous = previousNumber;

			if (IsExpectedNextNumeric(previous, current))
				return;

			var expectedDescription = previous.NumericPart + 1 > 0
				? $"{previous.NumericPart} lub {previous.NumericPart + 1}"
				: $"{previous.Value}";

			var message = $"Nieciagosc numeracji {unitLabel}: po '{previous.Value}' nastepuje '{current.Value}' " +
						  $"(oczekiwano NumericPart {expectedDescription}).";

			ValidationReporter.AddValidationMessage(entity, ValidationLevel.Warning, message);

			Log.Warning("Nieciagosc numeracji {UnitLabel} [{EntityId}]: {PreviousValue} -> {CurrentValue}",
				unitLabel, entity.Id, previous.Value, current.Value);
		}

		/// <summary>
		/// Waliduje ciągłość numeracji dla liter (LexicalPart: a → b → c ...).
		/// Dozwolone przejścia:
		///   - ten sam LexicalPart (np. a → a^1 — wariant z indeksem górnym)
		///   - następna litera alfabetu (np. a → b, c → d)
		/// </summary>
		private static void ValidateLetterNumbering(BaseEntity entity, EntityNumber? previousNumber)
		{
			if (previousNumber == null)
				return;

			var current = entity.Number!;
			var previous = previousNumber;

			if (IsExpectedNextLetter(previous, current))
				return;

			var expectedNext = GetNextLetterValue(previous.LexicalPart);
			var expectedDescription = !string.IsNullOrEmpty(expectedNext)
				? $"'{previous.LexicalPart}' lub '{expectedNext}'"
				: $"'{previous.LexicalPart}'";

			var message = $"Nieciagosc numeracji litery: po '{previous.Value}' nastepuje '{current.Value}' " +
						  $"(oczekiwano {expectedDescription}).";

			ValidationReporter.AddValidationMessage(entity, ValidationLevel.Warning, message);

			Log.Warning("Nieciagosc numeracji litery [{EntityId}]: {PreviousValue} -> {CurrentValue}",
				entity.Id, previous.Value, current.Value);
		}

		/// <summary>
		/// Sprawdza, czy bieżący numer jest oczekiwanym następnikiem poprzedniego
		/// (dla jednostek z NumericPart > 0).
		/// </summary>
		private static bool IsExpectedNextNumeric(EntityNumber previous, EntityNumber current)
		{
			// Ten sam NumericPart (np. 2 → 2a, 2a → 2b, 2 → 2^1)
			if (current.NumericPart == previous.NumericPart)
				return true;

			// Kolejny NumericPart (np. 2a → 3, 5 → 6)
			if (current.NumericPart == previous.NumericPart + 1)
				return true;

			return false;
		}

		/// <summary>
		/// Sprawdza, czy bieżąca litera jest oczekiwanym następnikiem poprzedniej
		/// (ciągłość alfabetyczna LexicalPart).
		/// </summary>
		private static bool IsExpectedNextLetter(EntityNumber previous, EntityNumber current)
		{
			// Ten sam LexicalPart (np. a → a^1 — wariant z indeksem)
			if (string.Equals(current.LexicalPart, previous.LexicalPart, StringComparison.OrdinalIgnoreCase))
				return true;

			// Następna litera (np. a → b, c → d)
			var expectedNext = GetNextLetterValue(previous.LexicalPart);
			if (!string.IsNullOrEmpty(expectedNext) &&
				string.Equals(current.LexicalPart, expectedNext, StringComparison.OrdinalIgnoreCase))
				return true;

			return false;
		}

		/// <summary>
		/// Oblicza następną wartość litery — deleguje do <see cref="NumberingHint.GetNextLetterValue"/>.
		/// </summary>
		internal static string? GetNextLetterValue(string? currentLetter)
			=> NumberingHint.GetNextLetterValue(currentLetter);
	}
}
