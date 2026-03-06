namespace WordParserCore.Services.Parsing.Builders
{
	/// <summary>
	/// Wspolny kontrakt dla builderow encji.
	/// </summary>
	public interface IEntityBuilder<in TInput, out TResult>
	{
		TResult Build(TInput input);
	}
}
