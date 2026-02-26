namespace WordParserLibrary.Services.Parsing.Classification.Layers
{
	/// <summary>
	/// Warstwa klasyfikacji na podstawie wzorców syntaktycznych tekstu.
	/// Deleguje logikę do ParagraphClassifier.Is*ByText (hub regex).
	/// Pewność: 90 dla wyraźnych dopasowań.
	/// </summary>
	internal sealed class SyntacticClassificationLayer : IClassificationLayer
	{
		public string LayerName => "Syntactic";

		public LayerClassificationResult? Classify(ClassificationInput input)
		{
			var text = input.Text;

			if (ParagraphClassifier.IsArticleByText(text))
				return Result(ParagraphKind.Article);
			if (ParagraphClassifier.IsParagraphByText(text))
				return Result(ParagraphKind.Paragraph);
			if (ParagraphClassifier.IsPointByText(text))
				return Result(ParagraphKind.Point);
			if (ParagraphClassifier.IsLetterByText(text))
				return Result(ParagraphKind.Letter);
			if (ParagraphClassifier.IsTiretByText(text))
				return Result(ParagraphKind.Tiret);

			return null;
		}

		private LayerClassificationResult Result(ParagraphKind kind) =>
			new() { LayerName = LayerName, Kind = kind, Confidence = 90 };
	}
}
