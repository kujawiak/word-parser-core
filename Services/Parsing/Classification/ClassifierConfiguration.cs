using System.Collections.Generic;

namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Konfiguracja wielowarstwowego klasyfikatora akapitów.
	/// </summary>
	public sealed class ClassifierConfiguration
	{
		/// <summary>
		/// Domyślna kolejność warstw: Style(waga 2) → Syntactic(waga 3) → Semantic(waga 2).
		/// </summary>
		public static readonly IReadOnlyList<LayerConfiguration> DefaultLayers =
		[
			new() { LayerName = "Style",     IsEnabled = true, Weight = 2 },
			new() { LayerName = "Syntactic", IsEnabled = true, Weight = 3 },
			new() { LayerName = "Semantic",  IsEnabled = true, Weight = 2 },
		];

		public IReadOnlyList<LayerConfiguration> Layers { get; init; } = DefaultLayers;

		public AiTriggerMode AiTriggerMode { get; init; } = AiTriggerMode.Disabled;

		/// <summary>
		/// Próg pewności (0–100), poniżej którego aktywowana jest warstwa AI (tryb OnLowConfidence).
		/// </summary>
		public int AiConfidenceThreshold { get; init; } = 60;

		/// <summary>
		/// Strategia rozstrzygania konfliktów między warstwami.
		/// Domyślnie ContentOverStyle — zachowuje obecne zachowanie ParagraphClassifier.
		/// </summary>
		public ConflictResolutionStrategy ConflictResolution { get; init; }
			= ConflictResolutionStrategy.ContentOverStyle;
	}
}
