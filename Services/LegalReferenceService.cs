using System.Text;
using System.Text.RegularExpressions;
using ModelDto;

namespace WordParserLibrary.Services
{
    /// <summary>
    /// Serwis do zarządzania odniesieniami do aktów prawnych.
    /// Odpowiada za aktualizację referencji na podstawie treści encji.
    /// </summary>
    public class LegalReferenceService
    {
        private static readonly Regex ArticleRef = new(
            @"(?:(?:po|Po|w|W)\s+)?art\.\s*([a-zA-Z0-9]+)",
            RegexOptions.Compiled);

        private static readonly Regex ParagraphRef = new(
            @"(?:(?:po|Po|w|W)\s+)?ust\.\s*([a-zA-Z0-9]+)",
            RegexOptions.Compiled);

        private static readonly Regex PointRef = new(
            @"(?:(?:po|Po|w|W)\s+)?pkt\s*([a-zA-Z0-9]+)",
            RegexOptions.Compiled);

        private static readonly Regex LetterRef = new(
            @"(?:(?:po|Po|w|W)\s+)?lit\.\s*([a-zA-Z])",
            RegexOptions.Compiled);

        /// <summary>
        /// Aktualizuje odniesienia w obiekcie StructuralReference na podstawie treści encji.
        /// Szuka wzorców takich jak "art. 5", "ust. 2", "pkt 3a", "lit. b", "tiret 1".
        /// </summary>
        public void UpdateLegalReference(StructuralReference reference, string contentText)
        {
            if (reference == null || string.IsNullOrEmpty(contentText))
                return;

            if (reference.Article == null)
            {
                var match = ArticleRef.Match(contentText);
                if (match.Success) reference.Article = match.Groups[1].Value;
            }

            if (reference.Paragraph == null)
            {
                var match = ParagraphRef.Match(contentText);
                if (match.Success) reference.Paragraph = match.Groups[1].Value;
            }

            if (reference.Point == null)
            {
                var match = PointRef.Match(contentText);
                if (match.Success) reference.Point = match.Groups[1].Value;
            }

            if (reference.Letter == null)
            {
                var match = LetterRef.Match(contentText);
                if (match.Success) reference.Letter = match.Groups[1].Value;
            }
        }

        /// <summary>
        /// Tworzy kontekst dla encji poprzez łączenie tekstów encji nadrzędnych.
        /// </summary>
        public string GetContext(BaseEntity entity)
        {
            var contextBuilder = new StringBuilder();
            var currentEntity = entity;

            while (currentEntity != null && !(currentEntity is ModelDto.EditorialUnits.Article))
            {
                if (!string.IsNullOrEmpty(currentEntity.ContentText))
                {
                    contextBuilder.Insert(0, currentEntity.ContentText + " ");
                }
                currentEntity = currentEntity.Parent;
            }

            return contextBuilder.ToString().Trim();
        }
    }
}
