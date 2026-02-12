using System;

namespace WordParserLibrary.Helpers
{
	/// <summary>
	/// Czym jest dokonywana zmiana (instrument nowelizacji).
	/// Wynika z prefiksu nazwy stylu (przed '/').
	/// </summary>
	public enum AmendmentInstrument
	{
		/// <summary>Z/ — zmiana artykułem (punktem) - 1. poziom nowelizacji</summary>
		ArticleOrPoint,
		/// <summary>Z_LIT/ — zmiana literą</summary>
		Letter,
		/// <summary>Z_TIR/ — zmiana tiretem</summary>
		Tiret,
		/// <summary>Z_2TIR/ — zmiana podwójnym tiretem</summary>
		DoubleTiret,
		/// <summary>ZZ/ — zmiana zmiany (2. poziom nowelizacji)</summary>
		Nested
	}

	/// <summary>
	/// Rodzaj jednostki docelowej nowelizacji (co jest zmieniane).
	/// Wynika z segmentu po '/' w nazwie stylu.
	/// </summary>
	public enum AmendmentTargetKind
	{
		/// <summary>ART(§) — artykuł / paragraf</summary>
		Article,
		/// <summary>UST(§) — ustęp / paragraf</summary>
		Paragraph,
		/// <summary>PKT — punkt</summary>
		Point,
		/// <summary>LIT — litera</summary>
		Letter,
		/// <summary>TIR — tiret</summary>
		Tiret,
		/// <summary>2TIR — podwójny tiret</summary>
		DoubleTiret,
		/// <summary>CZ_WSP_* — część wspólna (intro/wrapUp listy)</summary>
		CommonPart,
		/// <summary>FRAG / FRAGM — fragment (np. zdanie)</summary>
		Fragment,
		/// <summary>CYT — cytat (np. przysięga)</summary>
		Citation,
		/// <summary>S_KARN — sankcja karna</summary>
		PenalSanction,
		/// <summary>NIEART_TEKST — tekst nieartykułowany</summary>
		NonArticleText,
		/// <summary>ROZDZ/ODDZ/TYT/DZ/CZĘŚCI/KSIĘGI — jednostki systematyzujące</summary>
		SystematizingUnit,
		/// <summary>W_MAT(FIZ|CHEM) — wzór matematyczny/fizyczny/chemiczny</summary>
		Formula,
		/// <summary>LEG_W_MAT(FIZ|CHEM) — legenda wzoru</summary>
		FormulaLegend,
		/// <summary>ODNOŚNIKA — odnośnik (przypis)</summary>
		Footnote,
		/// <summary>Nierozpoznany typ jednostki</summary>
		Unknown
	}

	/// <summary>
	/// Zdekodowana informacja o stylu nowelizacji.
	/// Zawiera instrument zmiany, cel, kontekst nadrzedny i kod krotki.
	/// </summary>
	public sealed record AmendmentStyleInfo
	{
		/// <summary>Czym jest dokonywana zmiana (artykułem, literą, tiretem...)</summary>
		public required AmendmentInstrument Instrument { get; init; }

		/// <summary>Co jest zmieniane (artykuł, ustęp, punkt...)</summary>
		public required AmendmentTargetKind TargetKind { get; init; }

		/// <summary>
		/// Gdy TargetKind == CommonPart, jakiej listy dotyczy część wspólna.
		/// Np. CZ_WSP_PKT → CommonPartOf = Point.
		/// </summary>
		public AmendmentTargetKind? CommonPartOf { get; init; }

		/// <summary>
		/// Kontekst nadrzędny z segmentu '_w_' w nazwie stylu.
		/// Np. TIR_w_LIT → ParentContext = Letter (tiret wewnątrz litery).
		/// </summary>
		public AmendmentTargetKind? ParentContext { get; init; }

		/// <summary>Skrocona nazwa stylu (czesc przed ' – '), np. "Z/TIR_w_LIT"</summary>
		public required string ShortCode { get; init; }

		/// <summary>Pelna czytelna nazwa stylu z mapy</summary>
		public required string DisplayName { get; init; }
	}

	/// <summary>
	/// Prosty lookup styli nowelizacyjnych. Wszelkie metadane sa zapisane statycznie
	/// w <see cref="StyleLibraryMapper.AmendmentStyleInfoMap"/>.
	/// Nie wykonuje zadnego parsowania w runtime.
	/// </summary>
	public static class AmendmentStyleDecoder
	{
		/// <summary>
		/// Zwraca metadane stylu nowelizacyjnego po styleId z OpenXml.
		/// Prosty lookup w statycznej mapie — bez parsowania w runtime.
		/// </summary>
		public static AmendmentStyleInfo? DecodeByStyleId(string? styleId)
		{
			return StyleLibraryMapper.TryGetAmendmentStyleInfo(styleId);
		}
	}
}
