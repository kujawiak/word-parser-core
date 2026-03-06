using ModelDto;
using WordParserCore.Services;
using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoLetter = ModelDto.EditorialUnits.Letter;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;
using DtoPoint = ModelDto.EditorialUnits.Point;
using DtoTiret = ModelDto.EditorialUnits.Tiret;

namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wejscie dla budowania tiretu (litera + kontekst + indeks).
	/// </summary>
	public sealed record TiretBuildInput(
		DtoLetter Letter,
		DtoPoint? Point,
		DtoParagraph? Paragraph,
		DtoArticle Article,
		string Text,
		int Index,
		DtoTiret? ParentTiret = null);

	/// <summary>
	/// Builder tiretu: tworzy tiret i ustawia numer na podstawie indeksu.
	/// </summary>
	public sealed class TiretBuilder : IEntityBuilder<TiretBuildInput, DtoTiret>
	{
		private static readonly EntityNumberService _numberService = new();

		public DtoTiret Build(TiretBuildInput input)
		{
			var letter = input.Letter;
			var point = input.Point;
			var paragraph = input.Paragraph;
			var article = input.Article;
			var text = input.Text;
			var index = input.Index;
			var contentText = ParsingFactories.StripTiretPrefix(text);
			var tiret = new DtoTiret
			{
				Parent = (BaseEntity?)input.ParentTiret ?? letter,
				Article = article,
				Paragraph = paragraph,
				Point = point,
				Letter = letter,
				Number = _numberService.Create(numericPart: index)
			};
			ParsingFactories.SetContentAndSegments(tiret, contentText);

			if (input.ParentTiret != null)
				input.ParentTiret.Tirets.Add(tiret);
			else
				letter.Tirets.Add(tiret);
			return tiret;
		}

		public DtoTiret Build(DtoLetter letter, DtoPoint? point, DtoParagraph? paragraph, DtoArticle article, string text, int index)
		{
			return Build(new TiretBuildInput(letter, point, paragraph, article, text, index));
		}
	}
}
