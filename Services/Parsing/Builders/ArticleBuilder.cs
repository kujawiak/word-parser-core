using ModelDto.SystematizingUnits;
using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;

namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wejscie dla budowania artykulu (subchapter + tekst akapitu).
	/// </summary>
	public sealed record ArticleBuildInput(Subchapter Subchapter, string Text);

	/// <summary>
	/// Wynik budowania artykulu (artykul + pierwszy ustep).
	/// </summary>
	public sealed class ArticleBuildResult
	{
		public ArticleBuildResult(DtoArticle article, DtoParagraph paragraph)
		{
			Article = article;
			Paragraph = paragraph;
		}

		public DtoArticle Article { get; }
		public DtoParagraph Paragraph { get; }
	}

	/// <summary>
	/// Builder artykulu: tworzy Article oraz pierwszy ustep z ogona "Art.".
	/// </summary>
	public sealed class ArticleBuilder : IEntityBuilder<ArticleBuildInput, ArticleBuildResult>
	{
		public ArticleBuildResult Build(ArticleBuildInput input)
		{
			var subchapter = input.Subchapter;
			var text = input.Text;
			var article = new DtoArticle
			{
				Parent = subchapter,
				ContentText = text,
				Number = ParsingFactories.ParseArticleNumber(text)
			};

			subchapter.Articles.Add(article);

			var articleTail = ParsingFactories.GetArticleTail(text);
			var paragraph = ParsingFactories.CreateParagraphFromArticleTail(article, articleTail);
			article.Paragraphs.Add(paragraph);

			return new ArticleBuildResult(article, paragraph);
		}

		public ArticleBuildResult Build(Subchapter subchapter, string text)
		{
			return Build(new ArticleBuildInput(subchapter, text));
		}
	}
}
