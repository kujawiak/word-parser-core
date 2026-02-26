namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Strategia rozstrzygania konfliktów między warstwami klasyfikatora.
	/// </summary>
	public enum ConflictResolutionStrategy
	{
		/// <summary>
		/// Treść (analiza syntaktyczna) wygrywa nad stylem. Zachowuje obecne zachowanie ParagraphClassifier.
		/// </summary>
		ContentOverStyle,

		/// <summary>
		/// Ważona większość głosów ze wszystkich warstw.
		/// </summary>
		WeightedMajority,
	}
}
