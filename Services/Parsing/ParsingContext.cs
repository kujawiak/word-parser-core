using ModelDto;
using ModelDto.SystematizingUnits;
using DtoArticle = ModelDto.EditorialUnits.Article;
using DtoLetter = ModelDto.EditorialUnits.Letter;
using DtoParagraph = ModelDto.EditorialUnits.Paragraph;
using DtoPoint = ModelDto.EditorialUnits.Point;
using DtoTiret = ModelDto.EditorialUnits.Tiret;

namespace WordParserCore.Services.Parsing
{
	/// <summary>
	/// Kontekst parsowania przechowujacy aktualny stan drzewa encji
	/// oraz biezaca pozycje strukturalna w hierarchii jednostek redakcyjnych.
	/// </summary>
	public sealed class ParsingContext
	{
		public ParsingContext(LegalDocument document, Subchapter subchapter)
		{
			Document = document;
			Subchapter = subchapter;
		}

		public LegalDocument Document { get; }
		public Subchapter Subchapter { get; }
		public DtoArticle? CurrentArticle { get; set; }
		public DtoParagraph? CurrentParagraph { get; set; }
		public DtoPoint? CurrentPoint { get; set; }
		public DtoLetter? CurrentLetter { get; set; }

		/// <summary>
		/// Stos aktywnych tiretow wg glebokosci (0 = brak, 1 = TIR, 2 = 2TIR, 3 = 3TIR).
		/// Ostatni element to biezacy wlasciciel nowelizacji dla poziomu tiret.
		/// Czyszczony przy kazdym wejsciu na poziom Letter/Point/Paragraph/Article.
		/// </summary>
		public List<DtoTiret> TiretStack { get; } = new();

		/// <summary>Skrot: najglebszy aktywny tiret (lub null).</summary>
		public DtoTiret? CurrentTiret => TiretStack.Count > 0 ? TiretStack[^1] : null;

		/// <summary>
		/// Serwis do budowania i aktualizacji referencji strukturalnych
		/// w kontekscie nowelizacji.
		/// </summary>
		public LegalReferenceService ReferenceService { get; } = new();

		/// <summary>
		/// Biezaca pozycja strukturalna w hierarchii jednostek redakcyjnych
		/// (art. -> ust. -> pkt -> lit. -> tiret). Aktualizowana przez orkiestrator
		/// po kazdym zbudowaniu encji.
		/// </summary>
		public StructuralReference CurrentStructuralReference { get; set; } = new();

		/// <summary>
		/// Wykryte cele nowelizacji w tresci jednostek redakcyjnych.
		/// Klucz: Guid encji, Wartosc: wykryty cel (referencja strukturalna z RawText).
		/// Wypelniane przez orkiestrator podczas parsowania encji IHasAmendments.
		/// </summary>
		public Dictionary<Guid, StructuralAmendmentReference> DetectedAmendmentTargets { get; } = new();

		/// <summary>
		/// Czy aktualnie przetwarza akapity bedace trescia nowelizacji.
		/// Ustawiane na true gdy:
		/// - napotkano akapit ze stylem Z/... (Z/UST, Z/ART, Z/PKT itd.)
		/// - po triggerze ("otrzymuje brzmienie:") napotkano akapit bez stylu ustawy matki
		/// Resetowane gdy napotkano akapit z rozpoznanym stylem ustawy matki (ART, UST, PKT, LIT, TIR).
		/// </summary>
		public bool InsideAmendment { get; set; }

		/// <summary>
		/// Czy przetworzony wlasnie akapit zawieral zwrot rozpoczynajacy nowelizacje
		/// ("otrzymuje brzmienie:", "w brzmieniu:"). Ustawiane PO przetworzeniu
		/// akapitu, sprawdzane PRZED przetworzeniem nastepnego.
		/// </summary>
		public bool AmendmentTriggerDetected { get; set; }

		/// <summary>
		/// Bufor akapitow biezacej nowelizacji. Zbiera akapity od wejscia w tryb
		/// nowelizacji az do powrotu do stylu ustawy matki, po czym deleguje
		/// do AmendmentBuilder (iteracja 2).
		/// </summary>
		public AmendmentCollector AmendmentCollector { get; } = new();

		/// <summary>
		/// Encja, na ktorej wykryto ostatni trigger nowelizacji.
		/// Przechowywana tymczasowo do momentu rozpoczecia zbierania
		/// (Begin) w AmendmentCollector.
		/// </summary>
		public BaseEntity? AmendmentOwner { get; set; }

	}
}
