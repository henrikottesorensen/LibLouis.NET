namespace LibLouis.NET;

public class TranslatedString
{
    public required string Output { get; set; }

    public required int[] OutputPosition { get; set; }

    public required int[] InputPosition { get; set; }

    public required int CursorPosition { get; set; }

    /// <summary>
    /// Per output cell, whether liblouis reported the cell as containing dot 7 or dot 8.
    /// <see langword="null"/> when the translation ran without a formtype array, because liblouis
    /// only computes this when one is supplied.
    /// </summary>
    /// <remarks>
    /// This is the write-back half of the native typeform parameter. liblouis writes it per
    /// *output* cell, which is why it cannot go into the caller's input-sized formtype array -
    /// that write is exactly the buffer overrun the wrapper exists to prevent. Forward
    /// translation only: back-translation zero-fills the buffer and reports nothing.
    /// </remarks>
    public bool[]? OutputDots78 { get; set; }
}
