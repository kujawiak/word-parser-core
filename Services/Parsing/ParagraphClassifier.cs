using System;
using System.Text.RegularExpressions;
using WordParserLibrary.Helpers;

namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Klasyfikator akapitow. Rozpoznaje typ jednostki na bazie stylu i tekstu.
	/// Zawiera skompilowane wzorce regex wspoldzielone z ParsingFactories i AmendmentBuilder.
	/// </summary>
	public sealed class ParagraphClassifier : IParagraphClassifier
	{
		// ============================================================
		// Wspoldzielone wzorce regex (compiled, reużywane w ParsingFactories)
		// ============================================================

		/// <summary>Opcjonalny prefiks cytatu otwierajacego („ " ") w tekście akapitu.</summary>
		internal const string OptionalQuotePrefix = "(?:[\"\\u201E\\u201C\\u201D]\\s*)?";

		internal static readonly Regex ArticlePattern = new(
			$@"^{OptionalQuotePrefix}Art\.?\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static readonly Regex ParagraphPattern = new(
			$@"^{OptionalQuotePrefix}\d+[a-zA-Z]*\.\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static readonly Regex PointPattern = new(
			$@"^{OptionalQuotePrefix}\d+[a-zA-Z]*\)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static readonly Regex LetterPattern = new(
			$@"^{OptionalQuotePrefix}[a-zA-Z]{{1,5}}\)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		internal static readonly Regex TiretPattern = new(
			@"^\u2013+\s+", RegexOptions.Compiled);

		/// <summary>Artykuł z grupą przechwytującą numer (do ParseArticleNumber).</summary>
		internal static readonly Regex ArticleNumberCapture = new(
			$@"^{OptionalQuotePrefix}Art\.?\s*(\d+[a-zA-Z]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>Artykuł z przechwyceniem ogona (do GetArticleTail).</summary>
		internal static readonly Regex ArticleTailCapture = new(
			$@"^{OptionalQuotePrefix}Art\.?\s*\d+[a-zA-Z]*\.?\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>Numer ustępu z grupą przechwytującą (do ParseParagraphNumber).</summary>
		internal static readonly Regex ParagraphNumberCapture = new(
			$@"^{OptionalQuotePrefix}(\d+[a-zA-Z]*)\.\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>Numer punktu z grupą przechwytującą (do ParsePointNumber).</summary>
		internal static readonly Regex PointNumberCapture = new(
			$@"^{OptionalQuotePrefix}(\d+[a-zA-Z]*)\)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>Numer litery z grupą przechwytującą (do ParseLetterNumber).</summary>
		internal static readonly Regex LetterNumberCapture = new(
			$@"^{OptionalQuotePrefix}([a-zA-Z]{{1,5}})\)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>Prefiks tiretu do usuwania (bez wymagania spacji).</summary>
		internal static readonly Regex TiretStripPattern = new(
			@"^\u2013+\s*", RegexOptions.Compiled);
		/// <summary>
		/// Klasyfikuje akapit do typu jednostki (Art/Ust/Pkt/Lit/Tir).
		/// </summary>
		public ClassificationResult Classify(string text, string? styleId)
		{
			var styleType = GetStyleType(styleId);
			var isArticleByText = IsArticleByText(text);
			var isParagraphByText = IsParagraphByText(text);
			var isPointByText = IsPointByText(text);
			var isLetterByText = IsLetterByText(text);
			var isTiretByText = IsTiretByText(text);

			var result = new ClassificationResult
			{
				StyleType = styleType
			};

			// Sprawdz czy to styl nowelizacji (Z/...)
			if (styleType == "AMENDMENT")
			{
				result.IsAmendmentContent = true;
				return result;
			}

			if (isArticleByText)
			{
				result.Kind = ParagraphKind.Article;
				result.UsedFallback = styleType == null || !styleType.Equals("ART", StringComparison.OrdinalIgnoreCase);
				result.StyleTextConflict = styleType != null && !styleType.Equals("ART", StringComparison.OrdinalIgnoreCase);
				return result;
			}

			if (isParagraphByText || (!isPointByText && !isLetterByText && !isTiretByText && styleType == "UST"))
			{
				result.Kind = ParagraphKind.Paragraph;
				result.UsedFallback = isParagraphByText;
				result.StyleTextConflict = isParagraphByText && styleType != null && styleType != "UST";
				return result;
			}

			if (isPointByText || (!isParagraphByText && !isLetterByText && !isTiretByText && styleType == "PKT"))
			{
				result.Kind = ParagraphKind.Point;
				result.UsedFallback = isPointByText;
				result.StyleTextConflict = isPointByText && styleType != null && styleType != "PKT";
				return result;
			}

			if (isLetterByText || (!isParagraphByText && !isPointByText && !isTiretByText && styleType == "LIT"))
			{
				result.Kind = ParagraphKind.Letter;
				result.UsedFallback = isLetterByText;
				result.StyleTextConflict = isLetterByText && styleType != null && styleType != "LIT";
				return result;
			}

			if (isTiretByText || (!isParagraphByText && !isPointByText && !isLetterByText && styleType == "TIR"))
			{
				result.Kind = ParagraphKind.Tiret;
				result.UsedFallback = isTiretByText;
				result.StyleTextConflict = isTiretByText && styleType != null && styleType != "TIR";
				return result;
			}

			return result;
		}

		public static string? GetStyleType(string? styleId)
		{
			if (string.IsNullOrEmpty(styleId))
			{
				return null;
			}

			// Wykryj style nowelizacji po mapie styli (priorytet nad heurystyką).
			if (StyleLibraryMapper.TryGetStyleInfo(styleId, out var info) && info?.IsAmendment == true)
			{
				return "AMENDMENT";
			}

			// Fallback dla nietypowych styli nowelizacji spoza mapy.
			// Prefiksy: Z/ (zmiana artykułem/punktem), ZZ/ (zmiana zmiany),
			// Z_LIT/ (zmiana literą), Z_TIR/ (zmiana tiretem), Z_2TIR/ (podwójnym tiretem)
			if (styleId.StartsWith("Z/", StringComparison.OrdinalIgnoreCase) ||
				styleId.StartsWith("ZZ", StringComparison.OrdinalIgnoreCase) ||
				styleId.StartsWith("Z_", StringComparison.OrdinalIgnoreCase))
			{
				return "AMENDMENT";
			}

			if (styleId.StartsWith("ART", StringComparison.OrdinalIgnoreCase))
			{
				return "ART";
			}
			if (styleId.StartsWith("UST", StringComparison.OrdinalIgnoreCase))
			{
				return "UST";
			}
			if (styleId.StartsWith("PKT", StringComparison.OrdinalIgnoreCase))
			{
				return "PKT";
			}
			if (styleId.StartsWith("LIT", StringComparison.OrdinalIgnoreCase))
			{
				return "LIT";
			}
			if (styleId.StartsWith("TIR", StringComparison.OrdinalIgnoreCase))
			{
				return "TIR";
			}

			return null;
		}

		public static bool IsArticleByText(string text)
		{
			return ArticlePattern.IsMatch(text.Trim());
		}

		public static bool IsParagraphByText(string text)
		{
			return ParagraphPattern.IsMatch(text);
		}

		public static bool IsPointByText(string text)
		{
			return PointPattern.IsMatch(text);
		}

		public static bool IsLetterByText(string text)
		{
			return LetterPattern.IsMatch(text);
		}

		public static bool IsTiretByText(string text)
		{
			return TiretPattern.IsMatch(text);
		}
	}
}
