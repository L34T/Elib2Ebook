using System;

namespace Core.Exceptions;

public class Elib2EbookParseException : Elib2EbookException
{
    public Elib2EbookParseException()
    {
    }

    public Elib2EbookParseException(string message) : base(message)
    {
    }

    public Elib2EbookParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
