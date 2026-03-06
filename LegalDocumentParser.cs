using System;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Word = DocumentFormat.OpenXml.Wordprocessing;
using ModelDto;
using ModelDto.SystematizingUnits;
using WordParserCore.Exceptions;
using WordParserCore.Services.Parsing;

namespace WordParserCore
{
	public static class LegalDocumentParser
	{
		public static LegalDocument Parse(string filePath)
		{
			using var wordDoc = WordprocessingDocument.Open(filePath, false);
			return Parse(wordDoc);
		}

		public static LegalDocument Parse(WordprocessingDocument wordDocument)
		{
			var mainPart = wordDocument.MainDocumentPart ??
				throw new ParsingException("MainDocumentPart dokumentu jest null - plik moze byc uszkodzony lub pusty.");

			var document = new LegalDocument();
			var subchapter = GetDefaultSubchapter(document);

			var context = new ParsingContext(document, subchapter);
			var orchestrator = new ParserOrchestrator();

			foreach (var paragraph in mainPart.Document.Descendants<Word.Paragraph>())
			{
				orchestrator.ProcessParagraph(paragraph, context);
			}

			// Finalizacja — wypróżnienie bufora nowelizacji jeśli dokument
			// kończy się wewnątrz treści nowelizacji
			orchestrator.Finalize(context);

			return document;
		}

		private static Subchapter GetDefaultSubchapter(LegalDocument document)
		{
			var part = document.RootPart;
			var book = part.Books.First();
			var title = book.Titles.First();
			var division = title.Divisions.First();
			var chapter = division.Chapters.First();
			return chapter.Subchapters.First();
		}

	}
}
