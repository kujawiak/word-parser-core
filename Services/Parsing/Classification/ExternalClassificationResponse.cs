namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Odpowiedź klasyfikacji od zewnętrznego dostawcy AI.
	/// </summary>
	public sealed class ExternalClassificationResponse
	{
		public required ParagraphKind Kind { get; init; }
		/// <summary>Pewność klasyfikacji (0–100).</summary>
		public required int Confidence { get; init; }
		public string? Reasoning { get; init; }
	}
}
