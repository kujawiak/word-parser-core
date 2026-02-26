namespace WordParserLibrary.Services.Parsing.Classification.Layers
{
	/// <summary>
	/// Warstwa AI — wrapper dla zewnętrznego dostawcy klasyfikacji.
	/// Wywoływana jako ostatnia, zgodnie z konfiguracją AiTriggerMode.
	/// </summary>
	internal sealed class AiClassificationLayer(IExternalClassificationProvider provider)
		: IClassificationLayer
	{
		public string LayerName => "AI";

		public LayerClassificationResult? Classify(ClassificationInput input)
		{
			var response = provider.Classify(new ExternalClassificationRequest
			{
				Text                  = input.Text,
				StyleId               = input.StyleId,
				ArticleContext        = input.ArticleContext ?? [],
				PrecedingLayerResults = input.PrecedingLayerResults ?? [],
			});

			if (response is null)
				return null;

			return new LayerClassificationResult
			{
				LayerName         = LayerName,
				Kind              = response.Kind,
				Confidence        = response.Confidence,
				DiagnosticMessage = response.Reasoning,
			};
		}
	}
}
