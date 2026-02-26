using System.Collections.Generic;

namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Żądanie klasyfikacji kierowane do zewnętrznego dostawcy AI.
	/// </summary>
	public sealed class ExternalClassificationRequest
	{
		public required string Text { get; init; }
		public string? StyleId { get; init; }
		public IReadOnlyList<string> ArticleContext { get; init; } = [];
		public IReadOnlyList<LayerClassificationResult> PrecedingLayerResults { get; init; } = [];
	}
}
