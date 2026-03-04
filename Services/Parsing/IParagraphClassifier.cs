namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Kontrakt klasyfikatora akapitów w parserze.
	/// </summary>
	public interface IParagraphClassifier
	{
		ClassificationResult Classify(ClassificationInput input);
	}
}
