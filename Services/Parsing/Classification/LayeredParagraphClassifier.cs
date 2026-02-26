using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using WordParserLibrary.Services.Parsing.Classification.Layers;

namespace WordParserLibrary.Services.Parsing.Classification
{
	/// <summary>
	/// Wielowarstwowy klasyfikator akapitów. Implementuje <see cref="IParagraphClassifier"/>
	/// dla zachowania wstecznej zgodności.
	/// Obsługuje warstwy: Style → Syntactic → Semantic, opcjonalnie AI.
	/// </summary>
	public sealed class LayeredParagraphClassifier : IParagraphClassifier
	{
		private readonly ClassifierConfiguration _config;
		private readonly IReadOnlyList<IClassificationLayer> _layers;
		private readonly AiClassificationLayer? _aiLayer;

		public LayeredParagraphClassifier(
			ClassifierConfiguration? config = null,
			IExternalClassificationProvider? aiProvider = null)
		{
			_config  = config ?? new ClassifierConfiguration();
			_aiLayer = aiProvider != null ? new AiClassificationLayer(aiProvider) : null;
			_layers  = BuildLayers(_config.Layers);
		}

		private static IReadOnlyList<IClassificationLayer> BuildLayers(
			IReadOnlyList<LayerConfiguration> layerConfigs)
		{
			var available = new Dictionary<string, IClassificationLayer>(StringComparer.OrdinalIgnoreCase)
			{
				["Style"]     = new StyleClassificationLayer(),
				["Syntactic"] = new SyntacticClassificationLayer(),
				["Semantic"]  = new SemanticClassificationLayer(),
			};

			var result = new List<IClassificationLayer>();
			foreach (var cfg in layerConfigs)
			{
				if (!cfg.IsEnabled)
				{
					Log.Warning("Warstwa klasyfikacji {LayerName} jest wyłączona", cfg.LayerName);
					continue;
				}
				if (available.TryGetValue(cfg.LayerName, out var layer))
					result.Add(layer);
			}
			return result;
		}

		/// <summary>
		/// Implementacja <see cref="IParagraphClassifier.Classify"/> — wsteczna zgodność.
		/// </summary>
		public ClassificationResult Classify(string text, string? styleId)
			=> Classify(new ClassificationInput(text, styleId));

		/// <summary>
		/// Główna metoda klasyfikacji — przyjmuje pełny kontekst wejściowy z ArticleContext.
		/// </summary>
		public ClassificationResult Classify(ClassificationInput input)
		{
			var layerResults = new List<LayerClassificationResult>();

			foreach (var layer in _layers)
			{
				var enrichedInput = input with { PrecedingLayerResults = layerResults.AsReadOnly() };
				var result = layer.Classify(enrichedInput);
				if (result != null)
					layerResults.Add(result);
			}

			var preResult = BuildFinalResult(layerResults, _config.ConflictResolution);

			if (ShouldInvokeAi(layerResults, preResult) && _aiLayer != null)
			{
				var enrichedInput = input with { PrecedingLayerResults = layerResults.AsReadOnly() };
				var aiResult = _aiLayer.Classify(enrichedInput);
				if (aiResult != null)
				{
					layerResults.Add(aiResult);
					preResult = BuildFinalResult(layerResults, _config.ConflictResolution);
				}
			}

			preResult.LayerResults = layerResults.AsReadOnly();
			return preResult;
		}

		// ============================================================
		// Rozstrzyganie konfliktu i budowanie wyniku końcowego
		// ============================================================

		private ClassificationResult BuildFinalResult(
			IReadOnlyList<LayerClassificationResult> layerResults,
			ConflictResolutionStrategy strategy)
		{
			// Nowelizacja — najwyższy priorytet, niezależnie od strategii
			var amendmentLayer = layerResults.FirstOrDefault(r => r.IsAmendmentContent);
			if (amendmentLayer != null)
			{
				return new ClassificationResult
				{
					IsAmendmentContent = true,
					StyleType          = layerResults.FirstOrDefault(r => r.StyleType != null)?.StyleType,
					Confidence         = amendmentLayer.Confidence,
				};
			}

			LayerClassificationResult? finalLayer;
			if (strategy == ConflictResolutionStrategy.ContentOverStyle)
			{
				// Warstwa semantyczna już zastosowała logikę treść > styl.
				// Jeśli semantyczna nie ma werdyktu — sięgamy po wynik z każdej warstwy
				// POZA Style (warstwa Style jest tylko sygnałem wspierającym, nie decydującym).
				finalLayer = layerResults.LastOrDefault(r => r.LayerName == "Semantic" && r.Kind.HasValue)
				          ?? layerResults.LastOrDefault(r => r.Kind.HasValue && r.LayerName != "Style");
			}
			else
			{
				finalLayer = ApplyWeightedMajority(layerResults);
			}

			// Brak werdyktu — zwróć Unknown, ale zachowaj StyleType (np. "ART" bez sygnatury tekstu)
			if (finalLayer == null)
				return new ClassificationResult
				{
					StyleType = layerResults.FirstOrDefault(r => r.LayerName == "Style")?.StyleType,
				};

			var styleResult     = layerResults.FirstOrDefault(r => r.LayerName == "Style");
			var syntacticResult = layerResults.FirstOrDefault(r => r.LayerName == "Syntactic");

			bool hasStyleKind     = styleResult?.Kind.HasValue == true;
			bool hasSyntacticKind = syntacticResult?.Kind.HasValue == true;
			var  styleKind        = styleResult?.Kind;
			bool styleTextConflict = hasStyleKind && hasSyntacticKind
				&& styleResult!.Kind != syntacticResult!.Kind;

			var finalKind = finalLayer.Kind ?? ParagraphKind.Unknown;

			// UsedFallback — zachowuje semantykę ParagraphClassifier:
			// - Artykuł: true gdy tekst pasował, ale styl nie potwierdza (brak stylu lub inny)
			// - Pozostałe: true gdy tekst był zaangażowany w klasyfikację (niezależnie od stylu)
			bool usedFallback = finalKind == ParagraphKind.Article
				? hasSyntacticKind && (styleKind == null || styleKind != ParagraphKind.Article)
				: hasSyntacticKind;

			return new ClassificationResult
			{
				Kind              = finalKind,
				StyleType         = finalLayer.StyleType ?? styleResult?.StyleType,
				UsedFallback      = usedFallback,
				StyleTextConflict = styleTextConflict,
				Confidence        = finalLayer.Confidence,
			};
		}

		private LayerClassificationResult? ApplyWeightedMajority(
			IReadOnlyList<LayerClassificationResult> layerResults)
		{
			var kindScores = new Dictionary<ParagraphKind, int>();
			var kindBest   = new Dictionary<ParagraphKind, LayerClassificationResult>();

			foreach (var result in layerResults)
			{
				if (!result.Kind.HasValue)
					continue;
				var kind   = result.Kind.Value;
				var weight = _config.Layers.FirstOrDefault(l => l.LayerName == result.LayerName)?.Weight ?? 1;
				kindScores[kind] = kindScores.GetValueOrDefault(kind) + weight * result.Confidence;
				if (!kindBest.ContainsKey(kind) || result.Confidence > kindBest[kind].Confidence)
					kindBest[kind] = result;
			}

			if (!kindScores.Any())
				return null;

			var winner = kindScores.MaxBy(kvp => kvp.Value).Key;
			return kindBest[winner];
		}

		// ============================================================
		// Trigger warstwy AI
		// ============================================================

		private bool ShouldInvokeAi(
			IReadOnlyList<LayerClassificationResult> results,
			ClassificationResult pre)
			=> _config.AiTriggerMode switch
			{
				AiTriggerMode.Always          => _aiLayer is not null,
				AiTriggerMode.OnUnknown       => pre.Kind == ParagraphKind.Unknown,
				AiTriggerMode.OnConflict      => HasStyleSyntacticConflict(results),
				AiTriggerMode.OnLowConfidence => (pre.Confidence ?? 0) < _config.AiConfidenceThreshold,
				_                             => false,
			};

		private static bool HasStyleSyntacticConflict(IReadOnlyList<LayerClassificationResult> results)
		{
			var style     = results.FirstOrDefault(r => r.LayerName == "Style");
			var syntactic = results.FirstOrDefault(r => r.LayerName == "Syntactic");
			return style?.Kind.HasValue == true
				&& syntactic?.Kind.HasValue == true
				&& style.Kind != syntactic.Kind;
		}
	}
}
