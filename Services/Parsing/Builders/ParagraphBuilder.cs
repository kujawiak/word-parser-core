using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;

namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wejscie dla budowania ustepu (artykul + biezacy ustep + tekst).
	/// </summary>
	public sealed record ParagraphBuildInput(DtoArticle Article, DtoParagraph? CurrentParagraph, string Text);

	/// <summary>
	/// Wynik zapewnienia ustepu (niejawny/jawny).
	/// </summary>
	public sealed record ParagraphEnsureResult(DtoParagraph Paragraph, bool CreatedImplicit);

	/// <summary>
	/// Builder ustepu: tworzy lub uzupelnia ustep w kontekscie artykulu.
	/// </summary>
	public sealed class ParagraphBuilder : IEntityBuilder<ParagraphBuildInput, DtoParagraph>
	{
		public DtoParagraph Build(ParagraphBuildInput input)
		{
			var article = input.Article;
			var currentParagraph = input.CurrentParagraph;
			var text = input.Text;
			var contentText = ParsingFactories.StripParagraphPrefix(text);

			if (currentParagraph != null && currentParagraph.IsImplicit &&
				string.IsNullOrWhiteSpace(currentParagraph.ContentText) &&
				currentParagraph.Points.Count == 0)
			{
				currentParagraph.Number = ParsingFactories.ParseParagraphNumber(text);
				currentParagraph.IsImplicit = false;
				ParsingFactories.SetContentAndSegments(currentParagraph, contentText);
				return currentParagraph;
			}

			var paragraph = new DtoParagraph
			{
				Parent = article,
				Article = article,
				Number = ParsingFactories.ParseParagraphNumber(text),
				IsImplicit = false
			};
			ParsingFactories.SetContentAndSegments(paragraph, contentText);
			article.Paragraphs.Add(paragraph);
			return paragraph;
		}

		public DtoParagraph Build(DtoArticle article, DtoParagraph? currentParagraph, string text)
		{
			return Build(new ParagraphBuildInput(article, currentParagraph, text));
		}

		public ParagraphEnsureResult EnsureForPoint(DtoArticle article, DtoParagraph? currentParagraph)
		{
			if (currentParagraph != null)
			{
				return new ParagraphEnsureResult(currentParagraph, false);
			}

			var paragraph = ParsingFactories.CreateImplicitParagraph(article);
			article.Paragraphs.Add(paragraph);
			return new ParagraphEnsureResult(paragraph, true);
		}

		public DtoParagraph CreateImplicit(DtoArticle article)
		{
			return ParsingFactories.CreateImplicitParagraph(article);
		}
	}
}
