using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ModelDto;
using WordParserLibrary.Helpers;
using WordParserLibrary.Services.Parsing;

namespace WordParserLibrary.Services.Classify
{
	/// <summary>
	/// Klasyfikator akapitów. Rozpoznaje typ jednostki redakcyjnej i oblicza pewność (1–100).
	/// Rozwiązywanie konfliktów deleguje do <see cref="IConflictResolver"/>.
	/// </summary>
	public sealed class ParagraphClassifier : IParagraphClassifier
	{
		private readonly IConflictResolver       _conflictResolver;
		private readonly ConfidencePenaltyConfig _cfg;

		public ParagraphClassifier(
			IConflictResolver?       conflictResolver = null,
			ConfidencePenaltyConfig? penaltyConfig    = null)
		{
			_conflictResolver = conflictResolver ?? new DefaultConflictResolver();
			_cfg              = penaltyConfig    ?? ConfidencePenaltyConfig.Default;
		}

		// ============================================================
		// Współdzielone wzorce regex (reużywane przez ParsingFactories i AmendmentBuilder)
		// ============================================================

		/// <summary>Opcjonalny prefiks cytatu otwierającego („ " ") w tekście akapitu.</summary>
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
			@"^-+\s+", RegexOptions.Compiled);

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
			@"^-+\s*", RegexOptions.Compiled);

		// ============================================================
		// Implementacja IParagraphClassifier
		// ============================================================

		/// <summary>
		/// Klasyfikuje akapit do typu jednostki redakcyjnej i oblicza pewność (1–100).
		/// Confidence = 100 gdy styl, regex i numeracja są w pełnej zgodzie.
		/// </summary>
		public ClassificationResult Classify(ClassificationInput input)
		{
			var text      = input.Text;
			var styleType = GetStyleType(input.StyleId);
			var isAmend   = styleType == "AMENDMENT";

			// WrapUp — priorytet przed pozostałą logiką.
			// Wykrywany wyłącznie przez styl (CZ_WSP_*); tekst jedynie potwierdza lub obniża pewność.
			if (styleType == "WRAPUP")
				return BuildWrapUpResult(styleType, isAmend, byStyle: true, IsWrapUpByText(text));

			// Sygnał ze stylu (null gdy AMENDMENT lub nieznany)
			ParagraphKind? styleKind = isAmend ? null : MapStyleToKind(styleType);

			// Sygnał syntaktyczny (regex)
			ParagraphKind? syntacticKind = MatchRegex(text);

			return BuildResult(input, styleType, isAmend, styleKind, syntacticKind);
		}

		// ============================================================
		// Metody pomocnicze — publiczne statyczne (reużywane zewnętrznie)
		// ============================================================

		public static string? GetStyleType(string? styleId)
		{
			if (string.IsNullOrEmpty(styleId))
				return null;

			// Wykryj style nowelizacji po mapie styli (priorytet nad heurystyką)
			if (StyleLibraryMapper.TryGetStyleInfo(styleId, out var info) && info != null)
			{
				if (info.IsAmendment)
					return "AMENDMENT";

				// WrapUp — styl zamknięcia listy (CZ_WSP_*)
				if (info.DisplayName.StartsWith("CZ_WSP_", StringComparison.OrdinalIgnoreCase))
					return "WRAPUP";
			}

			// Fallback dla styli nowelizacji spoza mapy
			if (styleId.StartsWith("Z/",  StringComparison.OrdinalIgnoreCase) ||
			    styleId.StartsWith("ZZ",  StringComparison.OrdinalIgnoreCase) ||
			    styleId.StartsWith("Z_",  StringComparison.OrdinalIgnoreCase))
				return "AMENDMENT";

			if (styleId.StartsWith("ART", StringComparison.OrdinalIgnoreCase)) return "ART";
			if (styleId.StartsWith("UST", StringComparison.OrdinalIgnoreCase)) return "UST";
			if (styleId.StartsWith("PKT", StringComparison.OrdinalIgnoreCase)) return "PKT";
			if (styleId.StartsWith("LIT", StringComparison.OrdinalIgnoreCase)) return "LIT";
			if (styleId.StartsWith("TIR", StringComparison.OrdinalIgnoreCase)) return "TIR";
			if (styleId.StartsWith("2TIR", StringComparison.OrdinalIgnoreCase)) return "TIR";
			if (styleId.StartsWith("3TIR", StringComparison.OrdinalIgnoreCase)) return "TIR";

			return null;
		}

		public static bool IsArticleByText(string text)   => ArticlePattern.IsMatch(text.Trim());
		public static bool IsParagraphByText(string text) => ParagraphPattern.IsMatch(text);
		public static bool IsPointByText(string text)     => PointPattern.IsMatch(text);
		public static bool IsLetterByText(string text)    => LetterPattern.IsMatch(text);
		public static bool IsTiretByText(string text)     => TiretPattern.IsMatch(text);

		/// <summary>
		/// Sprawdza czy tekst to WrapUp (rozpoczyna się półpauzą lub dywizem po których stoi spacja).
		/// </summary>
		public static bool IsWrapUpByText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;
			var trimmed = text.TrimStart();
			if (trimmed.Length < 2)
				return false;
			var first = trimmed[0];
			return (first == '\u2013' || first == '-') && char.IsWhiteSpace(trimmed[1]);
		}

		// ============================================================
		// Prywatne metody budowania wyniku
		// ============================================================

		private ClassificationResult BuildWrapUpResult(
			string? styleType, bool isAmend, bool byStyle, bool byText)
		{
			var penalties  = new List<ClassificationPenalty>();
			int confidence = 100;

			if (!byStyle)
			{
				penalties.Add(new ClassificationPenalty
				{
					Reason = "Brak stylu WrapUp — typ ustalony wyłącznie z tekstu",
					Value  = _cfg.StyleAbsentPenalty,
				});
				confidence -= _cfg.StyleAbsentPenalty;
			}
			if (!byText)
			{
				penalties.Add(new ClassificationPenalty
				{
					Reason = "Brak półpauzy w tekście — typ ustalony wyłącznie ze stylu",
					Value  = _cfg.SyntaxAbsentPenalty,
				});
				confidence -= _cfg.SyntaxAbsentPenalty;
			}

			return new ClassificationResult
			{
				Kind               = ParagraphKind.WrapUp,
				Confidence         = Math.Clamp(confidence, 1, 100),
				IsAmendmentContent = isAmend,
				StyleType          = styleType,
				Penalties          = penalties,
			};
		}

		private ClassificationResult BuildResult(
			ClassificationInput input,
			string?             styleType,
			bool                isAmend,
			ParagraphKind?      styleKind,
			ParagraphKind?      syntacticKind)
		{
			var   penalties  = new List<ClassificationPenalty>();
			int   confidence = 100;
			ParagraphKind kind;

			if (styleKind == null && syntacticKind == null)
			{
				// Brak obu sygnałów
				kind       = ParagraphKind.Unknown;
				confidence = 1;
			}
			else if (styleKind != null && syntacticKind != null)
			{
				if (styleKind == syntacticKind)
				{
					// Oba sygnały zgodne
					kind = syntacticKind.Value;
				}
				else
				{
					// Konflikt — delegacja do IConflictResolver
					kind = _conflictResolver.Resolve(styleKind.Value, syntacticKind.Value, input.Text, input.StyleId);
					penalties.Add(new ClassificationPenalty
					{
						Reason = $"Konflikt: styl={styleKind}, regex={syntacticKind}; rozstrzygnięto: {kind}",
						Value  = _cfg.StyleSyntaxConflictPenalty,
					});
					confidence -= _cfg.StyleSyntaxConflictPenalty;
				}
			}
			else if (syntacticKind != null)
			{
				// Tylko regex
				kind = syntacticKind.Value;
				penalties.Add(new ClassificationPenalty
				{
					Reason = "Brak rozpoznanego stylu Word",
					Value  = _cfg.StyleAbsentPenalty,
				});
				confidence -= _cfg.StyleAbsentPenalty;
			}
			else
			{
				// Tylko styl — artykuł wymaga sygnatury tekstowej
				if (styleKind == ParagraphKind.Article)
				{
					kind       = ParagraphKind.Unknown;
					confidence = 1;
				}
				else
				{
					kind = styleKind!.Value;
					penalties.Add(new ClassificationPenalty
					{
						Reason = "Brak dopasowania regex — typ z samego stylu",
						Value  = _cfg.SyntaxAbsentPenalty,
					});
					confidence -= _cfg.SyntaxAbsentPenalty;
				}
			}

			// Sprawdzenie ciągłości numeracji (NumberingHint)
			if (input.NumberingHint is { } hint && hint.ExpectedKind == kind && kind != ParagraphKind.Unknown)
			{
				var parsedNumber = ParseNumberForKind(kind, input.Text);
				if (parsedNumber != null && !hint.IsContinuous(parsedNumber))
				{
					penalties.Add(new ClassificationPenalty
					{
						Reason = $"Nieciągłość numeracji {kind}: oczekiwano następnika {hint.ExpectedNumber?.Value}",
						Value  = _cfg.NumberingBreakPenalty,
					});
					confidence -= _cfg.NumberingBreakPenalty;
				}
			}

			return new ClassificationResult
			{
				Kind               = kind,
				Confidence         = Math.Clamp(confidence, 1, 100),
				IsAmendmentContent = isAmend,
				StyleType          = styleType,
				Penalties          = penalties,
			};
		}

		private static ParagraphKind? MapStyleToKind(string? styleType) =>
			styleType switch
			{
				"ART" => ParagraphKind.Article,
				"UST" => ParagraphKind.Paragraph,
				"PKT" => ParagraphKind.Point,
				"LIT" => ParagraphKind.Letter,
				"TIR" => ParagraphKind.Tiret,
				_     => null,
			};

		private static ParagraphKind? MatchRegex(string text)
		{
			if (IsArticleByText(text))   return ParagraphKind.Article;
			if (IsParagraphByText(text)) return ParagraphKind.Paragraph;
			if (IsPointByText(text))     return ParagraphKind.Point;
			if (IsLetterByText(text))    return ParagraphKind.Letter;
			if (IsTiretByText(text))     return ParagraphKind.Tiret;
			return null;
		}

		private static EntityNumber? ParseNumberForKind(ParagraphKind kind, string text) =>
			kind switch
			{
				ParagraphKind.Article   => ParsingFactories.ParseArticleNumber(text),
				ParagraphKind.Paragraph => ParsingFactories.ParseParagraphNumber(text),
				ParagraphKind.Point     => ParsingFactories.ParsePointNumber(text),
				ParagraphKind.Letter    => ParsingFactories.ParseLetterNumber(text),
				_                       => null,
			};
	}
}
