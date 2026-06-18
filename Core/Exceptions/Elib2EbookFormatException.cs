using System;

namespace Core.Exceptions;

public class Elib2EbookFormatException : Elib2EbookException
{
    public Elib2EbookFormatException()
    {
    }

    public Elib2EbookFormatException(string message) : base(message)
    {
    }

    public Elib2EbookFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
