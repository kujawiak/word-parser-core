namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Kontrakt pojedynczej warstwy klasyfikatora akapitów.
	/// </summary>
	public interface IClassificationLayer
	{
		string LayerName { get; }
		/// <summary>Klasyfikuje akapit; null = warstwa nie ma werdyktu.</summary>
		LayerClassificationResult? Classify(ClassificationInput input);
	}
}
