namespace WordParserLibrary.Services.Parsing.Classification.Layers
{
	/// <summary>
	/// Warstwa klasyfikacji na podstawie stylu akapitu Word.
	/// Deleguje logikę do ParagraphClassifier.GetStyleType (hub regex/styli).
	/// Pewność: 95 dla nowelizacji, 85 dla znanych styli strukturalnych.
	/// </summary>
	internal sealed class StyleClassificationLayer : IClassificationLayer
	{
		public string LayerName => "Style";

		public LayerClassificationResult? Classify(ClassificationInput input)
		{
			var styleType = ParagraphClassifier.GetStyleType(input.StyleId);
			if (styleType == null)
				return null;

			if (styleType == "AMENDMENT")
			{
				return new LayerClassificationResult
				{
					LayerName = LayerName,
					IsAmendmentContent = true,
					StyleType = styleType,
					Confidence = 95,
				};
			}

			var kind = styleType switch
			{
				"ART" => ParagraphKind.Article,
				"UST" => ParagraphKind.Paragraph,
				"PKT" => ParagraphKind.Point,
				"LIT" => ParagraphKind.Letter,
				"TIR" => ParagraphKind.Tiret,
				_     => (ParagraphKind?)null,
			};

			if (kind == null)
				return null;

			return new LayerClassificationResult
			{
				LayerName  = LayerName,
				Kind       = kind,
				StyleType  = styleType,
				Confidence = 85,
			};
		}
	}
}
