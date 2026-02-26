using System;

namespace WordParserLibrary.Exceptions
{
    /// <summary>
    /// Wyjątek zgłaszany, gdy hierarchia dokumentu jest nieprawidłowa lub niekompletna
    /// (np. brak wymaganej jednostki nadrzędnej).
    /// </summary>
    public class InvalidStructureException : ParsingException
    {
        public InvalidStructureException(string message) : base(message) { }
        public InvalidStructureException(string message, Exception innerException) : base(message, innerException) { }
    }
}
