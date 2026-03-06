using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;
using DtoPoint = ModelDto.EditorialUnits.Point;

namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wejscie dla budowania punktu (ustep + artykul + tekst).
	/// </summary>
	public sealed record PointBuildInput(DtoParagraph Paragraph, DtoArticle Article, string Text);

	/// <summary>
	/// Wynik zapewnienia punktu (niejawny/jawny).
	/// </summary>
	public sealed record PointEnsureResult(DtoPoint Point, bool CreatedImplicit);

	/// <summary>
	/// Builder punktu: tworzy punkt i parsuje numer z tekstu.
	/// </summary>
	public sealed class PointBuilder : IEntityBuilder<PointBuildInput, DtoPoint>
	{
		public DtoPoint Build(PointBuildInput input)
		{
			var paragraph = input.Paragraph;
			var article = input.Article;
			var text = input.Text;
			var contentText = ParsingFactories.StripPointPrefix(text);
			var point = new DtoPoint
			{
				Parent = paragraph,
				Article = article,
				Paragraph = paragraph,
				Number = ParsingFactories.ParsePointNumber(text)
			};
			ParsingFactories.SetContentAndSegments(point, contentText);

			paragraph.Points.Add(point);
			return point;
		}

		public DtoPoint Build(DtoParagraph paragraph, DtoArticle article, string text)
		{
			return Build(new PointBuildInput(paragraph, article, text));
		}

		public PointEnsureResult EnsureForLetter(DtoParagraph? paragraph, DtoArticle article, DtoPoint? currentPoint)
		{
			if (currentPoint != null)
			{
				return new PointEnsureResult(currentPoint, false);
			}

			var point = ParsingFactories.CreateImplicitPoint(paragraph, article);
			paragraph?.Points.Add(point);
			return new PointEnsureResult(point, true);
		}

		public DtoPoint CreateImplicit(DtoParagraph? paragraph, DtoArticle article)
		{
			return ParsingFactories.CreateImplicitPoint(paragraph, article);
		}
	}
}
