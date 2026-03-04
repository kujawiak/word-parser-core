namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Domyślna implementacja <see cref="IConflictResolver"/> — treść wygrywa nad stylem.
	/// Mock; właściwa implementacja zostanie zaprojektowana osobno.
	/// </summary>
	public sealed class DefaultConflictResolver : IConflictResolver
	{
		public ParagraphKind Resolve(
			ParagraphKind styleKind,
			ParagraphKind syntacticKind,
			string        text,
			string?       styleId)
			=> syntacticKind;
	}
}
