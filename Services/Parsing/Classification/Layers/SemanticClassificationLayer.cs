using System.Linq;

namespace WordParserLibrary.Services.Parsing.Classification.Layers
{
	/// <summary>
	/// Warstwa semantyczna — rozstrzyga konflikty między warstwą Style a Syntactic.
	/// Domyślna strategia: treść wygrywa nad stylem (Content > Style).
	/// Pewność: 95 gdy obie warstwy zgodne, 70 gdy tekst wygrywa nad stylem lub tylko styl.
	/// </summary>
	internal sealed class SemanticClassificationLayer : IClassificationLayer
	{
		public string LayerName => "Semantic";

		public LayerClassificationResult? Classify(ClassificationInput input)
		{
			var preceding = input.PrecedingLayerResults;
			if (preceding == null || preceding.Count == 0)
				return null;

			var styleResult    = preceding.FirstOrDefault(r => r.LayerName == "Style");
			var syntacticResult = preceding.FirstOrDefault(r => r.LayerName == "Syntactic");

			// Nowelizacja ma najwyższy priorytet — przepuszczamy bez zmian
			if (styleResult?.IsAmendmentContent == true)
			{
				return new LayerClassificationResult
				{
					LayerName        = LayerName,
					IsAmendmentContent = true,
					StyleType        = styleResult.StyleType,
					Confidence       = 95,
				};
			}

			var syntacticKind = syntacticResult?.Kind;
			var styleKind     = styleResult?.Kind;
			var styleType     = styleResult?.StyleType;

			// Treść wygrywa nad stylem (Content > Style)
			if (syntacticKind.HasValue)
			{
				bool consistent = styleKind == syntacticKind;
				return new LayerClassificationResult
				{
					LayerName          = LayerName,
					Kind               = syntacticKind,
					StyleType          = styleType,
					Confidence         = consistent ? 95 : 70,
					DiagnosticMessage  = !consistent && styleKind.HasValue
						? $"Konflikt: styl={styleKind}, tekst={syntacticKind}; przyjęto tekst"
						: null,
				};
			}

			// Tylko styl — zastosuj dla UST/PKT/LIT/TIR gdy brak sygnałów tekstowych.
			// NIE klasyfikujemy artykułu wyłącznie na podstawie stylu ART —
			// artykuł wymaga sygnatury tekstowej (zachowuje zachowanie ParagraphClassifier).
			if (styleKind.HasValue && styleKind != ParagraphKind.Article)
			{
				return new LayerClassificationResult
				{
					LayerName  = LayerName,
					Kind       = styleKind,
					StyleType  = styleType,
					Confidence = 70,
				};
			}

			return null;
		}
	}
}
