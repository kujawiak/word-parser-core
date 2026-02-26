using System.Collections.Generic;

namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Dane wejściowe dla warstwy klasyfikacji akapitu.
	/// </summary>
	public sealed record ClassificationInput(string Text, string? StyleId)
	{
		/// <summary>
		/// Wypełniane przez LayeredParagraphClassifier przed wywołaniem każdej warstwy.
		/// </summary>
		public IReadOnlyList<LayerClassificationResult>? PrecedingLayerResults { get; init; }

		/// <summary>
		/// Kontekst artykułu — lista tekstów poprzednich akapitów w bieżącym artykule.
		/// Dostarczane przez ParserOrchestrator (dla warstwy AI).
		/// </summary>
		public IReadOnlyList<string>? ArticleContext { get; init; }
	}
}
