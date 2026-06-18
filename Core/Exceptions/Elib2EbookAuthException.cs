using System;

namespace Core.Exceptions;

public class Elib2EbookAuthException : Elib2EbookException
{
    public Elib2EbookAuthException()
    {
    }

    public Elib2EbookAuthException(string message) : base(message)
    {
    }

    public Elib2EbookAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
