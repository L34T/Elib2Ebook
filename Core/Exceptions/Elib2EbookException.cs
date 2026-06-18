using System;

namespace Core.Exceptions;

public class Elib2EbookException : Exception
{
    public Elib2EbookException()
    {
    }

    public Elib2EbookException(string message) : base(message)
    {
    }

    public Elib2EbookException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
