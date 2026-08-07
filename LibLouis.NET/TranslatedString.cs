namespace LibLouis.NET;

public class TranslatedString
{
    public required string Output { get; set; }

    /// <summary>
    /// For each char of the input, the index into <see cref="Output"/> it translated to. One entry
    /// per char, so <c>OutputPosition.Length</c> equals the input's length and no slicing is
    /// needed. Both halves of a surrogate pair report the same position.
    /// </summary>
    /// <remarks>
    /// A UTF-16 index, usable directly against the strings. liblouis reports these in widechars -
    /// whole characters on a UCS-4 build - which agrees with UTF-16 only for BMP text; the
    /// wrapper translates them. This is not the array passed in, which stays as liblouis wrote it.
    /// </remarks>
    public required int[] OutputPosition { get; set; }

    /// <summary>
    /// For each char of <see cref="Output"/>, the index into the input it came from. One entry per
    /// char, so <c>InputPosition.Length</c> equals <c>Output.Length</c>.
    /// </summary>
    /// <remarks>
    /// A UTF-16 index, on the same terms as <see cref="OutputPosition"/>. Values always address
    /// the start of a character, never the trailing half of a surrogate pair.
    /// </remarks>
    public required int[] InputPosition { get; set; }

    /// <summary>
    /// Where the cursor ended up, as an index into <see cref="Output"/>. Negative when the
    /// translation was given no cursor.
    /// </summary>
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

    /// <summary>
    /// Per output cell, the spacing information liblouis reported: '*' where it reported nothing,
    /// an ASCII digit carried over from the input character that produced the cell, or '1' where
    /// back-translation inserted a space. <see langword="null"/> when the translation ran without a
    /// spacing argument, because liblouis only computes this when one is supplied.
    /// </summary>
    /// <remarks>
    /// This is the write-back half of the native spacing parameter, which is in/out: liblouis
    /// answers in the same buffer it reads the request from. It cannot be reported through the
    /// caller's <c>spacing</c> string - a .NET string is immutable, which is why the parameter
    /// silently did nothing before - and it is indexed per output cell rather than per input
    /// character, so it would not fit there anyway.
    /// <para>
    /// Shorter than <see cref="Output"/> when a forward translation grew the text: that direction
    /// only copies its answer back over as many bytes as the input was long, so the cells past that
    /// point have no answer. Back-translation reports the full output.
    /// </para>
    /// </remarks>
    public string? OutputSpacing { get; set; }
}
