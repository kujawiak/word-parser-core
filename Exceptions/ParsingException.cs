using System;

namespace WordParserLibrary.Exceptions
{
    /// <summary>
    /// Bazowy wyjątek dla błędów w trakcie parsowania dokumentu prawnego.
    /// </summary>
    public class ParsingException : Exception
    {
        public ParsingException(string message) : base(message) { }
        public ParsingException(string message, Exception innerException) : base(message, innerException) { }
    }
}
