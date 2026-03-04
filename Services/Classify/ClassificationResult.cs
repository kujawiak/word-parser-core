using System.Collections.Generic;

namespace WordParserLibrary.Services.Classify
{
	/// <summary>
	/// Wynik klasyfikacji akapitu: typ jednostki redakcyjnej, pewność (1–100)
	/// oraz opcjonalne kary diagnostyczne.
	/// IsAmendmentContent to cecha równoległa do Kind — akapit nowelizacji zachowuje swój Kind.
	/// </summary>
	public sealed class ClassificationResult
	{
		public ParagraphKind Kind             { get; init; } = ParagraphKind.Unknown;

		/// <summary>Pewność klasyfikacji w skali 1–100. 100 = pełna zgodność wszystkich sygnałów.</summary>
		public int Confidence { get; init; }

		/// <summary>
		/// True gdy akapit pochodzi z treści nowelizacji (styl Z/*).
		/// Nie wyklucza Kind — artykuł nowelizacji ma Kind=Article i IsAmendmentContent=true.
		/// </summary>
		public bool IsAmendmentContent { get; init; }

		/// <summary>Znormalizowany typ stylu Word ("ART","UST","PKT","LIT","TIR","WRAPUP","AMENDMENT",null).</summary>
		public string? StyleType { get; init; }

		/// <summary>Kary zastosowane podczas obliczania Confidence — do celów diagnostycznych.</summary>
		public IReadOnlyList<ClassificationPenalty> Penalties { get; init; } = [];
	}
}
