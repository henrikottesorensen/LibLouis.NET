using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// The wrapper passes inlen as input.Length + 1, which looks like it counts the NUL terminator
/// as a character to translate. It does not, and these tests pin that down so the "+ 1" is not
/// removed - or relied on - by mistake:
///
///   * lou_translateString clamps the length at the first NUL
///     (<c>while (k &lt; *inlen &amp;&amp; inbufx[k]) k++;</c>, lou_translateString.c:1191), so the
///     terminator is never translated.
///   * It then overwrites *inlen with the number of characters actually consumed
///     (lou_translateString.c:1354) before computing outputPos, so the inflated value cannot
///     reach the position loops and cannot push a write past the caller's array.
///
/// Both properties depend on the input buffer really being NUL terminated, which is
/// PrepareUCSInputBuffer's job.
/// </summary>
public class InputLengthTests
{
    private const string Input = "Første linje. Anden linje, med kursiveret tekst. Tredje linje.";

    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    /// <summary>
    /// Translate() only requires outputPosition to hold input.Length entries, so liblouis must
    /// not write beyond that. The array is deliberately oversized and sentinel filled, so an
    /// out-of-bounds write would be observable here instead of corrupting the heap.
    /// </summary>
    [Fact]
    public void Translate_DoesNotWriteOutputPositionsPastInputLength()
    {
        // Not -1: liblouis pre-fills outputPos with -1 for the characters it owns, so -1 could
        // not tell an untouched entry apart from one liblouis had written.
        const int sentinel = int.MinValue;
        const int slack = 8;

        int outputLength = Input.Length * 4;

        int[] outputPosition = new int[Input.Length + slack];
        Array.Fill(outputPosition, sentinel);

        LibLouis.Instance.Translate(
            TablePaths(),
            Input,
            outputLength,
            null,
            null,
            outputPosition,
            new int[outputLength],
            0,
            TranslationMode.Regular);

        int firstUntouched = Array.FindIndex(outputPosition, p => p == sentinel);

        Assert.Equal(Input.Length, firstUntouched);
    }

    /// <summary>
    /// The NUL terminator is not translated as if it were input text.
    /// </summary>
    [Fact]
    public void Translate_DoesNotTranslateTheNulTerminator()
    {
        const string input = "abc";

        string translated = LibLouis.Instance.Translate(
            TablePaths(), input, input.Length * 4, null, null, TranslationMode.Regular);

        Assert.DoesNotContain('\0', translated);
        Assert.Equal(input, translated);
    }
}
