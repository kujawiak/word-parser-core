namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Wynik klasyfikacji zwracany przez pojedynczą warstwę klasyfikatora.
	/// </summary>
	public sealed class LayerClassificationResult
	{
		public required string LayerName { get; init; }
		/// <summary>Rozpoznany typ jednostki; null = brak werdyktu.</summary>
		public ParagraphKind? Kind { get; init; }
		/// <summary>Typ stylu Word; wypełniany tylko przez warstwę Style.</summary>
		public string? StyleType { get; init; }
		public bool IsAmendmentContent { get; init; }
		/// <summary>Poziom pewności klasyfikacji (0–100).</summary>
		public int Confidence { get; init; }
		public string? DiagnosticMessage { get; init; }
	}
}
