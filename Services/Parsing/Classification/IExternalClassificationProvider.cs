namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Punkt integracji dla zewnętrznego klasyfikatora AI.
	/// Zewnętrzny projekt referencuje WordParserLibrary i implementuje ten interfejs.
	/// </summary>
	public interface IExternalClassificationProvider
	{
		ExternalClassificationResponse? Classify(ExternalClassificationRequest request);
	}
}
