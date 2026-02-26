namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Konfiguracja pojedynczej warstwy klasyfikatora.
	/// </summary>
	public sealed class LayerConfiguration
	{
		public required string LayerName { get; init; }
		public bool IsEnabled { get; init; } = true;
		/// <summary>
		/// Waga warstwy (1–10); wyższa = większy priorytet w strategii WeightedMajority.
		/// </summary>
		public int Weight { get; init; } = 1;
	}
}
