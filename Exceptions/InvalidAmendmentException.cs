using System;

namespace WordParserCore.Exceptions
{
    /// <summary>
    /// Wyjątek zgłaszany, gdy nowelizacja ma nieprawidłową strukturę lub brakuje wymaganych danych
    /// (np. brak właściciela, nieznany typ operacji).
    /// </summary>
    public class InvalidAmendmentException : ParsingException
    {
        public InvalidAmendmentException(string message) : base(message) { }
        public InvalidAmendmentException(string message, Exception innerException) : base(message, innerException) { }
    }
}
