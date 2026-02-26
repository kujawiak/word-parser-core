namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Tryb uruchamiania warstwy AI podczas klasyfikacji.
	/// </summary>
	public enum AiTriggerMode
	{
		/// <summary>Warstwa AI wyłączona.</summary>
		Disabled,
		/// <summary>Warstwa AI wywoływana zawsze.</summary>
		Always,
		/// <summary>Warstwa AI wywoływana gdy wynik to Unknown.</summary>
		OnUnknown,
		/// <summary>Warstwa AI wywoływana gdy styl i tekst są w konflikcie.</summary>
		OnConflict,
		/// <summary>Warstwa AI wywoływana gdy pewność klasyfikacji jest poniżej progu.</summary>
		OnLowConfidence,
	}
}
