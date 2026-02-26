using System.Collections.Generic;
using WordParserLibrary.Services.Parsing.Classification;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Wynik klasyfikacji akapitu: typ + informacje o konflikcie styl/tekst.
	/// </summary>
	public sealed class ClassificationResult
	{
		// Istniejące pola — bez zmian (wsteczna zgodność)
		public ParagraphKind Kind { get; set; } = ParagraphKind.Unknown;
		public string? StyleType { get; set; }
		public bool UsedFallback { get; set; }
		public bool StyleTextConflict { get; set; }
		public bool IsAmendmentContent { get; set; }

		// Nowe pola — null gdy pochodzi z ParagraphClassifier (tryb legacy)
		public int? Confidence { get; set; }
		public IReadOnlyList<LayerClassificationResult>? LayerResults { get; set; }
	}
}
