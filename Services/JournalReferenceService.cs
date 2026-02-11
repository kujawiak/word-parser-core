using System.Text.RegularExpressions;
using ModelDto;
using ModelDto.EditorialUnits;
using Serilog;

namespace WordParserLibrary.Services
{
	/// <summary>
	/// Serwis do parsowania publikatorów (Dz. U.) z treści artykułów
	/// i uzupełniania listy JournalInfo w encji Article.
	/// </summary>
	public class JournalReferenceService
	{
		// Regex dla publikatorów typu "Dz. U. z 2020 r. poz. 1234, 5678 i 9012"
		private static readonly Regex JournalRegex = new Regex(
			@"(?<source>Dz\.\s*U\.\s*(?:z\s*(?<year>\d{4})\s*r\.?)?\s*poz\.\s*(?<positions>[\d,\sandi]+(?:,\s*[\d,\sandi]+)*))",
			RegexOptions.Compiled | RegexOptions.IgnoreCase
		);

		/// <summary>
		/// Parsuje publikatory (Dz. U.) z treści artykułu i uzupełnia listę Journals.
		/// Grupuje pozycje po roku – jeśli ten sam rok wystąpi wielokrotnie,
		/// pozycje są scalane w jeden JournalInfo.
		/// </summary>
		public void ParseJournalReferences(Article article)
		{
			if (article == null || string.IsNullOrEmpty(article.ContentText))
				return;

			// Fallback roku: EffectiveDate artykułu, a jeśli nie ustawione — bieżący rok
			var fallbackYear = article.EffectiveDate.Year > 1
				? article.EffectiveDate.Year
				: DateTime.Now.Year;

			var journalsByYear = new Dictionary<int, JournalInfo>();

			foreach (Match m in JournalRegex.Matches(article.ContentText))
			{
				var sourceMatch = m.Groups["source"].Value;
				var yearStr = m.Groups["year"].Success ? m.Groups["year"].Value : null;
				var year = string.IsNullOrEmpty(yearStr) ? fallbackYear : int.Parse(yearStr);

				var positionsStr = m.Groups["positions"].Success ? m.Groups["positions"].Value : "";

				if (!journalsByYear.TryGetValue(year, out var journalInfo))
				{
					journalInfo = new JournalInfo { Year = year, SourceString = sourceMatch };
					journalsByYear[year] = journalInfo;
					article.Journals.Add(journalInfo);
				}
				else
				{
					// Aktualizuj SourceString jeśli nowe dopasowanie jest pełniejsze
					if (sourceMatch.Length > journalInfo.SourceString.Length)
					{
						journalInfo.SourceString = sourceMatch;
					}
				}

				var positionNumbers = positionsStr
					.Split(new[] { ',', ' ', 'i' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(s => int.TryParse(s.Trim(), out int num) ? num : (int?)null)
					.Where(n => n.HasValue)
					.Select(n => n!.Value)
					.ToList();

				journalInfo.Positions.AddRange(positionNumbers);
				journalInfo.Positions = journalInfo.Positions.Distinct().OrderBy(p => p).ToList();
			}

			if (article.Journals.Count > 0)
			{
				Log.Debug("Wykryto {Count} publikator(ów) w artykule [{ArticleId}]: {Journals}",
					article.Journals.Count,
					article.Id,
					string.Join("; ", article.Journals.Select(j => j.ToStringLong())));
			}
		}
	}
}
