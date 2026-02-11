using System.Linq;
using System.Text.RegularExpressions;
using ModelDto;
using ModelDto.EditorialUnits;
using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoLetter = ModelDto.EditorialUnits.Letter;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;
using DtoPoint = ModelDto.EditorialUnits.Point;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Fabryki pomocnicze dla parsowania: tworzenie encji, parsowanie numerow,
	/// usuwanie prefiksow numeracji i podzial tekstu na segmenty (zdania).
	/// </summary>
	public static class ParsingFactories
	{
		private static readonly EntityNumberService _numberService = new();

		// Regexy do usuwania prefiksu numeru z tresci
		private static readonly Regex ParagraphNumberPrefix = new(@"^\d+[a-zA-Z]*\.\s+", RegexOptions.Compiled);
		private static readonly Regex PointNumberPrefix = new(@"^\d+[a-zA-Z]*\)\s*", RegexOptions.Compiled);
		private static readonly Regex LetterNumberPrefix = new(@"^[a-zA-Z]{1,5}\)\s*", RegexOptions.Compiled);
		private static readonly Regex TiretPrefix = new(@"^\u2013+\s*", RegexOptions.Compiled);

		/// <summary>
		/// Regex podzialu na zdania: kropka, po ktorej wystepuje spacja
		/// i wielka litera (poczatek nowego zdania).
		/// </summary>
		private static readonly Regex SentenceSplitter = new(@"(?<=\.)\s+(?=[A-ZĄĆĘŁŃÓŚŹŻ])", RegexOptions.Compiled);

		/// <summary>
		/// Usuwa prefiks numeru ustepu (np. "1. ") z tekstu.
		/// </summary>
		public static string StripParagraphPrefix(string text)
			=> ParagraphNumberPrefix.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Usuwa prefiks numeru punktu (np. "1) ") z tekstu.
		/// </summary>
		public static string StripPointPrefix(string text)
			=> PointNumberPrefix.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Usuwa prefiks litery (np. "a) ") z tekstu.
		/// </summary>
		public static string StripLetterPrefix(string text)
			=> LetterNumberPrefix.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Usuwa prefiks tiretu (np. "– ") z tekstu.
		/// </summary>
		public static string StripTiretPrefix(string text)
			=> TiretPrefix.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Dzieli tekst na segmenty (zdania). Podział następuje w miejscu,
		/// gdzie po kropce i spacji pojawia się wielka litera.
		/// </summary>
		public static List<TextSegment> SplitIntoSentences(string text)
		{
			var segments = new List<TextSegment>();
			if (string.IsNullOrWhiteSpace(text))
				return segments;

			var parts = SentenceSplitter.Split(text);
			for (int i = 0; i < parts.Length; i++)
			{
				var part = parts[i].Trim();
				if (!string.IsNullOrEmpty(part))
				{
					segments.Add(new TextSegment
					{
						Type = TextSegmentType.Sentence,
						Text = part,
						Order = i + 1
					});
				}
			}
			return segments;
		}

		/// <summary>
		/// Ustawia ContentText (bez numeru) i TextSegments na encji implementujacej IHasTextSegments.
		/// </summary>
		public static void SetContentAndSegments(BaseEntity entity, string contentWithoutNumber)
		{
			entity.ContentText = contentWithoutNumber;
			if (entity is IHasTextSegments hasSegments)
			{
				hasSegments.TextSegments = SplitIntoSentences(contentWithoutNumber);
			}
		}
		public static DtoParagraph CreateImplicitParagraph(DtoArticle article)
		{
			return new DtoParagraph
			{
				Parent = article,
				Article = article,
				ContentText = string.Empty,
				IsImplicit = true
			};
		}

		public static DtoParagraph CreateParagraphFromArticleTail(DtoArticle article, string tail)
		{
			var normalizedTail = tail?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(normalizedTail))
			{
				return CreateImplicitParagraph(article);
			}

			var match = Regex.Match(normalizedTail, "^(\\d+[a-zA-Z]*)\\.\\s+", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				var contentText = StripParagraphPrefix(normalizedTail);
				var paragraph = new DtoParagraph
				{
					Parent = article,
					Article = article,
					Number = _numberService.Parse(match.Groups[1].Value),
					IsImplicit = false
				};
				SetContentAndSegments(paragraph, contentText);
				return paragraph;
			}

			var implicitParagraph = new DtoParagraph
			{
				Parent = article,
				Article = article,
				IsImplicit = true
			};
			SetContentAndSegments(implicitParagraph, normalizedTail);
			return implicitParagraph;
		}

		public static DtoPoint CreateImplicitPoint(DtoParagraph? paragraph, DtoArticle article)
		{
			return new DtoPoint
			{
				Parent = paragraph,
				Article = article,
				Paragraph = paragraph,
				ContentText = string.Empty
			};
		}

		public static DtoLetter CreateImplicitLetter(DtoPoint point, DtoParagraph? paragraph, DtoArticle article)
		{
			return new DtoLetter
			{
				Parent = point,
				Article = article,
				Paragraph = paragraph,
				Point = point,
				ContentText = string.Empty
			};
		}

		public static EntityNumber? ParseArticleNumber(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			var match = Regex.Match(text, "^Art\\.?\\s*(\\d+[a-zA-Z]*)", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				return _numberService.Parse(match.Groups[1].Value);
			}

			return null;
		}

		public static EntityNumber? ParseParagraphNumber(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			var match = Regex.Match(text.Trim(), "^(\\d+[a-zA-Z]*)\\.\\s+", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				return _numberService.Parse(match.Groups[1].Value);
			}

			return null;
		}

		public static EntityNumber? ParsePointNumber(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			var match = Regex.Match(text.Trim(), "^(\\d+[a-zA-Z]*)\\)\\s*", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				return _numberService.Parse(match.Groups[1].Value);
			}

			return null;
		}

		public static EntityNumber? ParseLetterNumber(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			var match = Regex.Match(text.Trim(), "^([a-zA-Z]{1,5})\\)\\s*", RegexOptions.IgnoreCase);
			return match.Success ? _numberService.Parse(match.Groups[1].Value) : null;
		}

		public static string GetArticleTail(string text)
		{
			var match = Regex.Match(text.Trim(), "^Art\\.?\\s*\\d+[a-zA-Z]*\\.?\\s*(.*)$", RegexOptions.IgnoreCase);
			return match.Success ? match.Groups[1].Value : string.Empty;
		}

		/// <summary>
		/// Tworzy CommonPart typu Intro i wiaze go z odpowiednim segmentem tekstu rodzica.
		/// Logika:
		///   - 1 segment  -> caly segment jest Intro
		///   - N segmentow -> ostatni segment jest Intro
		/// Warunki wstepne: rodzic implementuje IHasCommonParts i IHasTextSegments,
		/// posiada segmenty tekstu i nie ma jeszcze CommonPart Intro.
		/// </summary>
		public static void AttachIntroCommonPart(BaseEntity parent)
		{
			if (parent is not IHasCommonParts hasCommonParts) return;
			if (parent is not IHasTextSegments hasTextSegments) return;

			var segments = hasTextSegments.TextSegments;
			if (segments.Count == 0) return;

			// Nie dodawaj ponownie jesli intro juz istnieje
			if (hasCommonParts.CommonParts.Any(cp => cp.Type == CommonPartType.Intro))
				return;

			// 1 segment -> caly jest intro; wiele segmentow -> ostatni jest intro
			var introSegment = segments.Count == 1 ? segments[0] : segments[^1];
			introSegment.Role = "ListIntro";

			var commonPart = new CommonPart(
				CommonPartType.Intro,
				parent.Id,
				introSegment.Text,
				introSegment.Order)
			{
				Parent = parent
			};

			hasCommonParts.CommonParts.Add(commonPart);
		}
	}
}
