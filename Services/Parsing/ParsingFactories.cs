using System.Linq;
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

		/// <summary>
		/// Usuwa prefiks numeru ustepu (np. "1. ") z tekstu.
		/// </summary>
		public static string StripParagraphPrefix(string text)
			=> ParagraphClassifier.ParagraphPattern.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Usuwa prefiks numeru punktu (np. "1) ") z tekstu.
		/// </summary>
		public static string StripPointPrefix(string text)
			=> ParagraphClassifier.PointPattern.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Usuwa prefiks litery (np. "a) ") z tekstu.
		/// </summary>
		public static string StripLetterPrefix(string text)
			=> ParagraphClassifier.LetterPattern.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Usuwa prefiks tiretu (np. "– ") z tekstu.
		/// </summary>
		public static string StripTiretPrefix(string text)
			=> ParagraphClassifier.TiretStripPattern.Replace(text.Trim(), "", 1);

		/// <summary>
		/// Dzieli tekst na segmenty (zdania). Podział następuje w miejscu,
		/// gdzie po kropce i spacji pojawia się wielka litera, z wyjątkami:
		/// - "RRRR r." (rok) - nie jest punktem podziału
		/// - "Dz. U." (czasopismo) - nie jest punktem podziału
		/// </summary>
		public static List<TextSegment> SplitIntoSentences(string text)
		{
			var segments = new List<TextSegment>();
			if (string.IsNullOrWhiteSpace(text))
				return segments;

			var sentences = SplitBySentenceWithExceptions(text);
			for (int i = 0; i < sentences.Count; i++)
			{
				var sentence = sentences[i].Trim();
				if (!string.IsNullOrEmpty(sentence))
				{
					segments.Add(new TextSegment
					{
						Type = TextSegmentType.Sentence,
						Text = sentence,
						Order = i + 1
					});
				}
			}
			return segments;
		}

		/// <summary>
		/// Dzieli tekst na zdania z uwzględnieniem wyjątków:
		/// - Nie dzieli po "RRRR r." (rok)
		/// - Nie dzieli po "Dz. U." (czasopismo)
		/// </summary>
		private static List<string> SplitBySentenceWithExceptions(string text)
		{
			var result = new List<string>();
			if (string.IsNullOrWhiteSpace(text))
				return result;

			int startIndex = 0;
			for (int i = 0; i < text.Length - 1; i++)
			{
				if (text[i] == '.' && i + 1 < text.Length && text[i + 1] == ' ')
				{
					// Znaleziono potencjalny koniec zdania (kropka + spacja)
					// Sprawdź czy następuje wielka litera
					int nextCharIndex = i + 2;
					while (nextCharIndex < text.Length && char.IsWhiteSpace(text[nextCharIndex]))
					{
						nextCharIndex++;
					}

					if (nextCharIndex >= text.Length || !IsUpperCaseLetter(text[nextCharIndex]))
					{
						// Brak wielkiej litery - to nie koniec zdania
						continue;
					}

					// Jest wielka litera, ale sprawdź wyjątki
					if (IsRokException(text, i) || IsDzUException(text, i))
					{
						// To jest wyjątek - nie dziel tutaj
						continue;
					}

					// To jest prawdziwy koniec zdania
					string sentence = text.Substring(startIndex, i - startIndex + 1);
					result.Add(sentence);
					startIndex = i + 2;
				}
			}

			// Dodaj pozostałą część tekstu jako ostatnie zdanie
			if (startIndex < text.Length)
			{
				result.Add(text.Substring(startIndex));
			}

			return result;
		}

		/// <summary>
		/// Sprawdza czy kropka na pozycji `dotIndex` jest częścią wyrażenia "RRRR r."
		/// </summary>
		private static bool IsRokException(string text, int dotIndex)
		{
			// Szukamy wzorca: cyfra cyfra cyfra cyfra spacja r . 
			// tj. "RRRR r."
			if (dotIndex < 7) return false;

			// Sprawdź czy jest " r." przed dotIndex
			if (text[dotIndex - 1] != 'r' || text[dotIndex - 2] != ' ')
				return false;

			// Sprawdź czy poprzedzające to 4 cyfry (rok)
			// text[dotIndex - 3] powinno być spacją lub cyfrą
			int checkPos = dotIndex - 3;
			int digitCount = 0;
			while (checkPos >= 0 && char.IsDigit(text[checkPos]))
			{
				digitCount++;
				checkPos--;
			}

			// Powinna być dokładnie 4 cyfry na rok
			return digitCount == 4;
		}

		/// <summary>
		/// Sprawdza czy kropka na pozycji `dotIndex` jest częścią wyrażenia "Dz. U."
		/// </summary>
		private static bool IsDzUException(string text, int dotIndex)
		{
			// Szukamy wyrażenia "Dz. U." gdzie dotIndex to pozycja kropki
			// Przypadki:
			// - "Dz. U. z..." gdzie dotIndex = pozycja kropki po "Dz"
			// - "Dz.U. z..." gdzie dotIndex = pozycja kropki po "Dz"
			// - Lub dotIndex może być później, np. na "U."
			
			// Jeśli pozycja wskazuje na "Dz.", sprawdź czy następuje "U"
			if (dotIndex > 0 && (text[dotIndex - 1] == 'z' || text[dotIndex - 1] == 'Z'))
			{
				// Mamy "z.", tj. koniec "Dz."
				// Patrzym czy wcześniej jest "D"
				if (dotIndex >= 2 && (text[dotIndex - 2] == 'D' || text[dotIndex - 2] == 'd'))
				{
					// Mamy "Dz."
					// Patrzym czy dalej jest " U" lub " u"
					int nextPos = dotIndex + 1;
					while (nextPos < text.Length && text[nextPos] == ' ')
						nextPos++;
					
					if (nextPos < text.Length && (text[nextPos] == 'U' || text[nextPos] == 'u'))
					{
						// Dalej jest "U", czyli mamy "Dz. U" - to wyjątek
						return true;
					}
				}
			}

			// Sprawdzenie dla "U."
			if (dotIndex > 0 && (text[dotIndex - 1] == 'U' || text[dotIndex - 1] == 'u'))
			{
				// Mamy "U."
				// Patrzym czy wcześniej jest "Dz. " lub "Dz."
				int searchPos = dotIndex - 2;
				
				// Pomiń spacje
				while (searchPos >= 0 && text[searchPos] == ' ')
					searchPos--;
				
				// Sprawdzenie czy jest "."
				if (searchPos >= 0 && text[searchPos] == '.')
				{
					searchPos--;
					// Sprawdzenie czy jest 'z' lub 'Z'
					if (searchPos >= 0 && (text[searchPos] == 'z' || text[searchPos] == 'Z'))
					{
						searchPos--;
						// Pomiń spacje
						while (searchPos >= 0 && text[searchPos] == ' ')
							searchPos--;
						
						// Sprawdzenie czy jest 'D' lub 'd'
						if (searchPos >= 0 && (text[searchPos] == 'D' || text[searchPos] == 'd'))
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Sprawdza czy znak to wielka litera (w tym polskie)
		/// </summary>
		private static bool IsUpperCaseLetter(char c)
		{
			return char.IsUpper(c) || "ĄĆĘŁŃÓŚŹŻ".Contains(c);
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

			var match = ParagraphClassifier.ParagraphNumberCapture.Match(normalizedTail);
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

			var match = ParagraphClassifier.ArticleNumberCapture.Match(text);
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

			var match = ParagraphClassifier.ParagraphNumberCapture.Match(text.Trim());
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

			var match = ParagraphClassifier.PointNumberCapture.Match(text.Trim());
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

			var match = ParagraphClassifier.LetterNumberCapture.Match(text.Trim());
			return match.Success ? _numberService.Parse(match.Groups[1].Value) : null;
		}

		public static string GetArticleTail(string text)
		{
			var match = ParagraphClassifier.ArticleTailCapture.Match(text.Trim());
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
