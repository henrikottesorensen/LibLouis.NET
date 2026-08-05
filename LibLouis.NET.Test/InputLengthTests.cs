using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// inlen is a widechar count that excludes the NUL terminator, matching the header and what
/// upstream callers pass. These tests pin down the two properties that depend on it:
///
///   * The terminator is not translated as if it were text. The buffer stays NUL terminated
///     (PrepareUCSInputBuffer's job) and lou_translateString clamps at the first NUL
///     (<c>while (k &lt; *inlen &amp;&amp; inbufx[k]) k++;</c>, lou_translateString.c:1191), so an
///     embedded NUL still ends the input.
///   * Nothing is written past the position arrays the argument checks demand. liblouis
///     overwrites *inlen with the number of characters actually consumed
///     (lou_translateString.c:1354) before computing outputPos.
///
/// The wrapper previously passed input.Length + 1 here. That was safe - the clamp at :1191 and
/// the overwrite at :1354 between them made the extra count unreachable - but it left
/// correctness resting on two undocumented internals instead of the documented contract.
/// </summary>
public class InputLengthTests
{
    private const string Input = "Første linje. Anden linje, med kursiveret tekst. Tredje linje.";

    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nota-tables", t))];

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
