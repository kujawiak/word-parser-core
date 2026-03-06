using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoLetter = ModelDto.EditorialUnits.Letter;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;
using DtoPoint = ModelDto.EditorialUnits.Point;

namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wejscie dla budowania litery (punkt + ustep + artykul + tekst).
	/// </summary>
	public sealed record LetterBuildInput(DtoPoint Point, DtoParagraph? Paragraph, DtoArticle Article, string Text);

	/// <summary>
	/// Wynik zapewnienia litery (niejawna/jawna).
	/// </summary>
	public sealed record LetterEnsureResult(DtoLetter Letter, bool CreatedImplicit);

	/// <summary>
	/// Builder litery: tworzy litere i parsuje jej oznaczenie.
	/// </summary>
	public sealed class LetterBuilder : IEntityBuilder<LetterBuildInput, DtoLetter>
	{
		public DtoLetter Build(LetterBuildInput input)
		{
			var point = input.Point;
			var paragraph = input.Paragraph;
			var article = input.Article;
			var text = input.Text;
			var contentText = ParsingFactories.StripLetterPrefix(text);
			var letter = new DtoLetter
			{
				Parent = point,
				Article = article,
				Paragraph = paragraph,
				Point = point,
				Number = ParsingFactories.ParseLetterNumber(text)
			};
			ParsingFactories.SetContentAndSegments(letter, contentText);

			point.Letters.Add(letter);
			return letter;
		}

		public DtoLetter Build(DtoPoint point, DtoParagraph? paragraph, DtoArticle article, string text)
		{
			return Build(new LetterBuildInput(point, paragraph, article, text));
		}

		public LetterEnsureResult EnsureForTiret(DtoPoint point, DtoParagraph? paragraph, DtoArticle article, DtoLetter? currentLetter)
		{
			if (currentLetter != null)
			{
				return new LetterEnsureResult(currentLetter, false);
			}

			var letter = ParsingFactories.CreateImplicitLetter(point, paragraph, article);
			point.Letters.Add(letter);
			return new LetterEnsureResult(letter, true);
		}

		public DtoLetter CreateImplicit(DtoPoint point, DtoParagraph? paragraph, DtoArticle article)
		{
			return ParsingFactories.CreateImplicitLetter(point, paragraph, article);
		}
	}
}
