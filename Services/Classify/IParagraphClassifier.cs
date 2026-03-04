namespace WordParserLibrary.Services.Classify
{
	/// <summary>
	/// Kontrakt klasyfikatora akapitów w parserze.
	/// </summary>
	public interface IParagraphClassifier
	{
		ClassificationResult Classify(ClassificationInput input);
	}
}
