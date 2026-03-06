namespace WordParserCore.Services.Classify
{
	/// <summary>
	/// Rozstrzyga konflikt między sygnałem stylu a sygnałem syntaktycznym (regex).
	/// Wywoływany przez ParagraphClassifier gdy oba sygnały wskazują różne typy jednostki.
	/// </summary>
	public interface IConflictResolver
	{
		ParagraphKind Resolve(
			ParagraphKind styleKind,
			ParagraphKind syntacticKind,
			string        text,
			string?       styleId);
	}
}
