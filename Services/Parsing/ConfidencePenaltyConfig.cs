namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Konfigurowalne kary zmniejszające Confidence klasyfikacji.
	/// Wszystkie wartości są domyślne i można je nadpisać przy tworzeniu instancji.
	/// </summary>
	public sealed class ConfidencePenaltyConfig
	{
		public static readonly ConfidencePenaltyConfig Default = new();

		/// <summary>
		/// Kara gdy styl Word jest null lub nierozpoznany — brakuje sygnału ze stylu.
		/// </summary>
		public int StyleAbsentPenalty { get; init; } = 10;

		/// <summary>
		/// Kara gdy żaden wzorzec regex nie pasuje — typ ustalony wyłącznie na podstawie stylu.
		/// </summary>
		public int SyntaxAbsentPenalty { get; init; } = 15;

		/// <summary>
		/// Kara gdy styl i regex wskazują różne typy jednostki (konflikt).
		/// Delegacja rozstrzygnięcia do <see cref="IConflictResolver"/>.
		/// </summary>
		public int StyleSyntaxConflictPenalty { get; init; } = 25;

		/// <summary>
		/// Kara gdy numer jednostki zaburza ciągłość numeracji (NumberingHint).
		/// </summary>
		public int NumberingBreakPenalty { get; init; } = 10;
	}
}
