using ModelDto;
using WordParserCore.Services.Classify;

namespace WordParserCore.Services.Parsing
{
	/// <summary>
	/// Rejestruje ostrzeżenia i komunikaty walidacji na encjach DTO.
	/// </summary>
	public static class ValidationReporter
	{
		/// <summary>
		/// Dodaje ostrzeżenie walidacyjne gdy klasyfikacja miała niską pewność (Confidence &lt; 100).
		/// Każda kara klasyfikatora jest rejestrowana jako osobny komunikat.
		/// </summary>
		public static void AddClassificationWarning(
			BaseEntity           entity,
			ClassificationResult classification,
			string               expectedType)
		{
			if (classification.Confidence >= 100 || classification.Penalties.Count == 0)
				return;

			foreach (var penalty in classification.Penalties)
			{
				AddValidationMessage(entity, ValidationLevel.Warning,
					$"{expectedType}: {penalty.Reason} (kara -{penalty.Value})");
			}
		}

		public static void AddValidationMessage(BaseEntity entity, ValidationLevel level, string message)
		{
			entity.ValidationMessages.Add(new ValidationMessage(level, message));
		}
	}
}
