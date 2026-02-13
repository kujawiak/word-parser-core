using System.Text;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WordParserLibrary
{
    public static class ParagraphExtensions
    {
        public static string? StyleId(this Paragraph paragraph)
        {
            return paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.ToString();
        }

        public static bool? StyleId(this Paragraph paragraph, string styleId)
        {
            return paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.ToString()?.StartsWith(styleId);
        }

        /// <summary>
        /// Ekstrahuje pelny tekst z akapitu, uwzgledniajac elementy pomijane
        /// przez standardowe InnerText:
        /// - SymbolChar (w:sym font="Symbol" char="F02D") → en-dash (\u2013)
        /// - FootnoteReference (w:footnoteReference) → numer przypisu
        /// - TabChar (w:tab) → tabulator
        /// - Tekst w indeksie gornym (styl IGindeksgrny, Odwoanieprzypisudolnego
        ///   lub VerticalAlignment=Superscript) jest ujmowany w nawiasy [ ].
        /// </summary>
        public static string GetFullText(this Paragraph paragraph)
        {
            var sb = new StringBuilder();
            bool inSuperscript = false;

            foreach (var run in paragraph.Descendants<Run>())
            {
                bool isSuperscript = IsSuperscriptRun(run);

                if (isSuperscript && !inSuperscript)
                {
                    sb.Append('[');
                    inSuperscript = true;
                }
                else if (!isSuperscript && inSuperscript)
                {
                    sb.Append(']');
                    inSuperscript = false;
                }

                foreach (var child in run.ChildElements)
                {
                    switch (child)
                    {
                        case Text text:
                            sb.Append(text.Text);
                            break;

                        case SymbolChar sym
                            when string.Equals(sym.Font?.Value, "Symbol", StringComparison.OrdinalIgnoreCase)
                              && string.Equals(sym.Char?.Value, "F02D", StringComparison.OrdinalIgnoreCase):
                            sb.Append('\u2013');
                            break;

                        case FootnoteReference fnRef:
                            sb.Append(fnRef.Id?.Value);
                            break;

                        case TabChar:
                            sb.Append('\t');
                            break;
                    }
                }
            }

            if (inSuperscript)
                sb.Append(']');

            return sb.ToString();
        }

        private static bool IsSuperscriptRun(Run run)
        {
            var rp = run.RunProperties;
            if (rp == null)
                return false;

            var styleVal = rp.RunStyle?.Val?.Value;
            if (styleVal is "IGindeksgrny" or "Odwoanieprzypisudolnego")
                return true;

            if (rp.VerticalTextAlignment?.Val?.Value == VerticalPositionValues.Superscript)
                return true;

            return false;
        }
    }
}