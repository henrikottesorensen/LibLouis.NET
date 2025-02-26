using System;

namespace LibLouis.NET;

public class LibLouisException : Exception
{
    public LibLouisException()
    {
    }

    public LibLouisException(string? message)
        : base(message)
    {
    }

    public LibLouisException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
