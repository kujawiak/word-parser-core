using System;

namespace WordParserCore.Exceptions
{
    /// <summary>
    /// Wyjątek zgłaszany, gdy klasyfikacja akapitu jest niemożliwa lub prowadzi do niejednoznacznego wyniku
    /// nie dającego się naprawić przez logikę semantyczną.
    /// </summary>
    public class ClassificationException : ParsingException
    {
        public ClassificationException(string message) : base(message) { }
        public ClassificationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
