namespace WordParserLibrary.Services.Parsing
{
	/// <summary>
	/// Kara obniżająca pewność klasyfikacji wraz z opisem powodu.
	/// </summary>
	public sealed class ClassificationPenalty
	{
		public required string Reason { get; init; }
		public required int    Value  { get; init; }
	}
}
