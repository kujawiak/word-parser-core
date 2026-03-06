using System;
using System.Collections.Generic;
using ModelDto;
using ModelDto.EditorialUnits;
using WordParserCore.Helpers;
using WordParserCore.Services;
using WordParserCore.Services.Classify;
using Serilog;

namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wejście dla budowania treści nowelizacji z zebranych akapitów.
	/// </summary>
	public sealed record AmendmentBuildInput(
		IReadOnlyList<CollectedAmendmentParagraph> Paragraphs,
		StructuralAmendmentReference? Target,
		AmendmentOperationType OperationType);

	/// <summary>
	/// Typ klasyfikacji akapitu w treści nowelizacji.
	/// Mapuje AmendmentTargetKind (ze stylu) lub wzorzec tekstowy (fallback)
	/// na rodzaj encji do zbudowania.
	/// </summary>
	public enum AmendmentEntityKind
	{
		Article,
		Paragraph,
		Point,
		Letter,
		Tiret,
		CommonPart,
		PlainText,
		Unknown
	}

	/// <summary>
	/// Builder treści nowelizacji. Przetwarza bufor akapitów zebranych przez AmendmentCollector
	/// i buduje hierarchiczną strukturę AmendmentContent, analogicznie do głównego orkiestratora.
	///
	/// Klasyfikacja wewnętrzna opiera się na AmendmentStyleInfo.TargetKind (z dekodera stylów),
	/// z fallbackiem na wzorce tekstowe (regex) gdy styl jest nieznany.
	///
	/// Hierarchia budowania: ART → UST → PKT → LIT → TIR.
	/// Encje zagnieżdżone trafiają do odpowiednich kolekcji rodziców,
	/// a encje najwyższego poziomu trafiają bezpośrednio do AmendmentContent.
	/// </summary>
	public sealed class AmendmentBuilder : IEntityBuilder<AmendmentBuildInput, AmendmentContent>
	{
		private static readonly EntityNumberService NumberService = new();

		// Bieżący stan hierarchii podczas budowania
		private Article? _currentArticle;
		private Paragraph? _currentParagraph;
		private Point? _currentPoint;
		private Letter? _currentLetter;
		private int _currentTiretIndex;

		/// <summary>
		/// Buduje AmendmentContent z listy zebranych akapitów nowelizacji.
		/// Klasyfikuje każdy akapit, buduje encje i organizuje je hierarchicznie.
		/// </summary>
		public AmendmentContent Build(AmendmentBuildInput input)
		{
			var content = new AmendmentContent();

			if (input.Paragraphs.Count == 0)
			{
				content.ObjectType = AmendmentObjectType.None;
				Log.Debug("AmendmentBuilder: brak akapitów do przetworzenia");
				return content;
			}

			ResetState();

			content.ObjectType = DetermineObjectType(input.Paragraphs);

			foreach (var para in input.Paragraphs)
			{
				ProcessAmendmentParagraph(para, content);
			}

			Log.Information(
				"AmendmentBuilder: zbudowano treść nowelizacji ({ObjectType}): {Content}",
				content.ObjectType, content);

			return content;
		}

		// ============================================================
		// Klasyfikacja akapitów nowelizacji
		// ============================================================

		/// <summary>
		/// Klasyfikuje pojedynczy akapit nowelizacji na podstawie AmendmentStyleInfo
		/// (priorytet) lub wzorców tekstowych (fallback).
		/// </summary>
		internal static AmendmentEntityKind ClassifyAmendmentParagraph(CollectedAmendmentParagraph para)
		{
			// Priorytet 1: dekodowanie z metadanych stylu
			if (para.StyleInfo != null)
			{
				return MapTargetKindToEntityKind(para.StyleInfo.TargetKind);
			}

			// Priorytet 2: klasyfikacja tekstowa (te same regexy co ParagraphClassifier)
			if (ParagraphClassifier.IsArticleByText(para.Text))
				return AmendmentEntityKind.Article;
			if (ParagraphClassifier.IsParagraphByText(para.Text))
				return AmendmentEntityKind.Paragraph;
			if (ParagraphClassifier.IsPointByText(para.Text))
				return AmendmentEntityKind.Point;
			if (ParagraphClassifier.IsLetterByText(para.Text))
				return AmendmentEntityKind.Letter;
			if (ParagraphClassifier.IsTiretByText(para.Text))
				return AmendmentEntityKind.Tiret;

			return AmendmentEntityKind.Unknown;
		}

		/// <summary>
		/// Mapuje AmendmentTargetKind (z dekodera stylów) na AmendmentEntityKind
		/// (wewnętrzny typ klasyfikacji do budowania).
		/// </summary>
		internal static AmendmentEntityKind MapTargetKindToEntityKind(AmendmentTargetKind targetKind)
		{
			return targetKind switch
			{
				AmendmentTargetKind.Article => AmendmentEntityKind.Article,
				AmendmentTargetKind.Paragraph => AmendmentEntityKind.Paragraph,
				AmendmentTargetKind.Point => AmendmentEntityKind.Point,
				AmendmentTargetKind.Letter => AmendmentEntityKind.Letter,
				AmendmentTargetKind.Tiret => AmendmentEntityKind.Tiret,
				AmendmentTargetKind.DoubleTiret => AmendmentEntityKind.Tiret,
				AmendmentTargetKind.CommonPart => AmendmentEntityKind.CommonPart,
				AmendmentTargetKind.Fragment => AmendmentEntityKind.PlainText,
				AmendmentTargetKind.Citation => AmendmentEntityKind.PlainText,
				AmendmentTargetKind.PenalSanction => AmendmentEntityKind.PlainText,
				AmendmentTargetKind.NonArticleText => AmendmentEntityKind.PlainText,
				_ => AmendmentEntityKind.Unknown
			};
		}

		/// <summary>
		/// Wyznacza typ obiektu nowelizacji (ObjectType) na podstawie pierwszego
		/// sklasyfikowanego akapitu w buforze.
		/// </summary>
		internal static AmendmentObjectType DetermineObjectType(
			IReadOnlyList<CollectedAmendmentParagraph> paragraphs)
		{
			foreach (var para in paragraphs)
			{
				var kind = ClassifyAmendmentParagraph(para);
				return kind switch
				{
					AmendmentEntityKind.Article => AmendmentObjectType.Article,
					AmendmentEntityKind.Paragraph => AmendmentObjectType.Paragraph,
					AmendmentEntityKind.Point => AmendmentObjectType.Point,
					AmendmentEntityKind.Letter => AmendmentObjectType.Letter,
					AmendmentEntityKind.Tiret => AmendmentObjectType.Tiret,
					AmendmentEntityKind.CommonPart => AmendmentObjectType.CommonPart,
					_ => AmendmentObjectType.None
				};
			}
			return AmendmentObjectType.None;
		}

		// ============================================================
		// Przetwarzanie pojedynczego akapitu
		// ============================================================

		private void ProcessAmendmentParagraph(CollectedAmendmentParagraph para, AmendmentContent content)
		{
			var entityKind = ClassifyAmendmentParagraph(para);

			switch (entityKind)
			{
				case AmendmentEntityKind.Article:
					BuildAmendmentArticle(para.Text, content);
					break;
				case AmendmentEntityKind.Paragraph:
					BuildAmendmentParagraph(para.Text, content);
					break;
				case AmendmentEntityKind.Point:
					BuildAmendmentPoint(para.Text, content);
					break;
				case AmendmentEntityKind.Letter:
					BuildAmendmentLetter(para.Text, content);
					break;
				case AmendmentEntityKind.Tiret:
					BuildAmendmentTiret(para.Text, content);
					break;
				case AmendmentEntityKind.CommonPart:
					BuildAmendmentCommonPart(para, content);
					break;
				default:
					AppendPlainText(content, para.Text);
					Log.Debug("AmendmentBuilder: akapit niesklasyfikowany, dodano jako tekst: {Text}",
						para.Text.Length > 60 ? para.Text[..60] + "..." : para.Text);
					break;
			}
		}

		// ============================================================
		// Budowanie poszczególnych typów encji
		// ============================================================

		private void BuildAmendmentArticle(string text, AmendmentContent content)
		{
			var article = new Article
			{
				ContentText = text,
				Number = ParsingFactories.ParseArticleNumber(text)
			};

			var articleTail = ParsingFactories.GetArticleTail(text);
			var paragraph = ParsingFactories.CreateParagraphFromArticleTail(article, articleTail);
			article.Paragraphs.Add(paragraph);

			content.Articles.Add(article);

			_currentArticle = article;
			_currentParagraph = paragraph;
			_currentPoint = null;
			_currentLetter = null;
			_currentTiretIndex = 0;

			Log.Debug("AmendmentBuilder: artykuł {Number}", article.Number?.Value ?? "?");
		}

		private void BuildAmendmentParagraph(string text, AmendmentContent content)
		{
			var contentText = ParsingFactories.StripParagraphPrefix(text);
			var number = ParsingFactories.ParseParagraphNumber(text);

			if (_currentArticle != null)
			{
				// Sprawdź czy mozna ponownie użyć niejawnego pierwszego ustępu
				var first = _currentArticle.Paragraphs.Count == 1
					? _currentArticle.Paragraphs[0]
					: null;

				if (first is { IsImplicit: true }
					&& string.IsNullOrWhiteSpace(first.ContentText)
					&& first.Points.Count == 0)
				{
					first.Number = number;
					first.IsImplicit = false;
					ParsingFactories.SetContentAndSegments(first, contentText);
					_currentParagraph = first;
				}
				else
				{
					var paragraph = new Paragraph
					{
						Parent = _currentArticle,
						Article = _currentArticle,
						Number = number,
						IsImplicit = false
					};
					ParsingFactories.SetContentAndSegments(paragraph, contentText);
					_currentArticle.Paragraphs.Add(paragraph);
					_currentParagraph = paragraph;
				}
			}
			else
			{
				// Ustęp najwyższego poziomu (bez kontekstu artykułu)
				var paragraph = new Paragraph
				{
					Number = number,
					IsImplicit = false
				};
				ParsingFactories.SetContentAndSegments(paragraph, contentText);
				content.Paragraphs.Add(paragraph);
				_currentParagraph = paragraph;
			}

			_currentPoint = null;
			_currentLetter = null;
			_currentTiretIndex = 0;

			Log.Debug("AmendmentBuilder: ustęp {Number}", _currentParagraph.Number?.Value ?? "?");
		}

		private void BuildAmendmentPoint(string text, AmendmentContent content)
		{
			var contentText = ParsingFactories.StripPointPrefix(text);
			var point = new Point
			{
				Parent = _currentParagraph,
				Article = _currentArticle,
				Paragraph = _currentParagraph,
				Number = ParsingFactories.ParsePointNumber(text)
			};
			ParsingFactories.SetContentAndSegments(point, contentText);

			if (_currentParagraph != null)
			{
				// Wiąż intro przed pierwszym punktem
				if (_currentParagraph.Points.Count == 0)
					ParsingFactories.AttachIntroCommonPart(_currentParagraph);

				_currentParagraph.Points.Add(point);
			}
			else
			{
				// Punkt najwyższego poziomu
				content.Points.Add(point);
			}

			_currentPoint = point;
			_currentLetter = null;
			_currentTiretIndex = 0;

			Log.Debug("AmendmentBuilder: punkt {Number}", point.Number?.Value ?? "?");
		}

		private void BuildAmendmentLetter(string text, AmendmentContent content)
		{
			var contentText = ParsingFactories.StripLetterPrefix(text);
			var letter = new Letter
			{
				Parent = _currentPoint,
				Article = _currentArticle,
				Paragraph = _currentParagraph,
				Point = _currentPoint,
				Number = ParsingFactories.ParseLetterNumber(text)
			};
			ParsingFactories.SetContentAndSegments(letter, contentText);

			if (_currentPoint != null)
			{
				if (_currentPoint.Letters.Count == 0)
					ParsingFactories.AttachIntroCommonPart(_currentPoint);

				_currentPoint.Letters.Add(letter);
			}
			else
			{
				content.Letters.Add(letter);
			}

			_currentLetter = letter;
			_currentTiretIndex = 0;

			Log.Debug("AmendmentBuilder: litera {Number}", letter.Number?.Value ?? "?");
		}

		private void BuildAmendmentTiret(string text, AmendmentContent content)
		{
			var contentText = ParsingFactories.StripTiretPrefix(text);
			_currentTiretIndex++;

			var tiret = new Tiret
			{
				Parent = _currentLetter,
				Article = _currentArticle,
				Paragraph = _currentParagraph,
				Point = _currentPoint,
				Letter = _currentLetter,
				Number = NumberService.Create(numericPart: _currentTiretIndex)
			};
			ParsingFactories.SetContentAndSegments(tiret, contentText);

			if (_currentLetter != null)
			{
				if (_currentLetter.Tirets.Count == 0)
					ParsingFactories.AttachIntroCommonPart(_currentLetter);

				_currentLetter.Tirets.Add(tiret);
			}
			else
			{
				content.Tirets.Add(tiret);
			}

			Log.Debug("AmendmentBuilder: tiret {Number}", tiret.Number?.Value ?? "?");
		}

		private void BuildAmendmentCommonPart(CollectedAmendmentParagraph para, AmendmentContent content)
		{
			var commonPartOf = para.StyleInfo?.CommonPartOf;

			// Ustal rodzica na podstawie CommonPartOf — część wspólna
			// jest rodzeństwem elementów wyliczeniowych
			BaseEntity? parent = commonPartOf switch
			{
				AmendmentTargetKind.Point => _currentParagraph,
				AmendmentTargetKind.Letter => _currentPoint,
				AmendmentTargetKind.Tiret => _currentLetter,
				_ => _currentParagraph
			};

			var commonPart = new CommonPart(
				CommonPartType.WrapUp,
				parent?.Id ?? string.Empty,
				para.Text)
			{
				Parent = parent
			};

			if (parent is IHasCommonParts hasCommonParts)
			{
				hasCommonParts.CommonParts.Add(commonPart);
			}
			else
			{
				content.CommonParts.Add(commonPart);
			}

			Log.Debug("AmendmentBuilder: część wspólna ({Type}) dla {ParentId}",
				commonPart.Type, commonPart.ParentEId);
		}

		// ============================================================
		// Helpers
		// ============================================================

		private static void AppendPlainText(AmendmentContent content, string text)
		{
			content.PlainText = string.IsNullOrEmpty(content.PlainText)
				? text
				: content.PlainText + Environment.NewLine + text;
		}

		private void ResetState()
		{
			_currentArticle = null;
			_currentParagraph = null;
			_currentPoint = null;
			_currentLetter = null;
			_currentTiretIndex = 0;
		}
	}
}
