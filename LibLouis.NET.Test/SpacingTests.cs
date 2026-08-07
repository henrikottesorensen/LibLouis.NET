using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// The spacing parameter is in/out: liblouis answers in the same buffer it reads the request from.
/// It used to be declared as a string, so the answer landed in the marshaller's temporary and was
/// freed unread, and the buffer was sized to the input while liblouis writes per output cell.
/// </summary>
public class SpacingTests
{
    private static readonly string[] Tables =
        new[] { "da-dk-braillo.dis", "da-dk-g26.ctb" }
            .Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))
            .ToArray();

    private static TranslatedString Translate(string input, string? spacing, int outputLength)
    {
        return LibLouis.Instance.Translate(
            Tables, input, outputLength, null, spacing,
            new int[input.Length], new int[outputLength], -1, TranslationMode.Regular);
    }

    private static TranslatedString BackTranslate(string input, string? spacing, int outputLength)
    {
        return LibLouis.Instance.BackTranslate(
            Tables, input, outputLength, null, spacing,
            new int[input.Length], new int[outputLength], -1, TranslationMode.Regular);
    }

    [Fact]
    public void Spacing_IsNotReported_WhenNotRequested()
    {
        TranslatedString translated = Translate("Anden linje, med kursiveret tekst.", null, 200);

        Assert.Null(translated.OutputSpacing);
    }

    [Fact]
    public void Spacing_ReportsTheRequestedDigits_AgainstTheOutputCellsTheyProduced()
    {
        const string input = "Anden linje, med kursiveret tekst.";

        // Mark the three characters of "lin" in "linje".
        char[] request = new string('0', input.Length).ToCharArray();
        request[6] = request[7] = request[8] = '3';

        TranslatedString translated = Translate(input, new string(request), 200);

        Assert.Equal("@anç linje, m kursi#rò ükz.", translated.Output);
        Assert.Equal("**0003330000000000000000000", translated.OutputSpacing);

        // One entry per output cell: '*' where liblouis reported nothing for a cell, otherwise the
        // digit belonging to the input character that produced it.
        Assert.Equal(translated.Output.Length, translated.OutputSpacing!.Length);

        for (int cell = 0; cell < translated.OutputSpacing.Length; cell++)
        {
            if (translated.OutputSpacing[cell] == '3')
            {
                Assert.Contains(translated.InputPosition[cell], new[] { 6, 7, 8 });
            }
        }
    }

    [Fact]
    public void Spacing_IsClippedToTheInputLength_WhenTheOutputGrows()
    {
        // Forward translation copies its answer back over only the first inlen bytes of the buffer,
        // so a translation that grows past the input reports nothing for the cells beyond it. The
        // marker table expands "yes" into a foreign-language run.
        const string input = "Han sagde yes.";

        char[] request = new string('7', input.Length).ToCharArray();

        string[] markerTables =
            new[] { "da-dk-braillo.dis", "da-dk-g16-markers.ctb" }
                .Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))
                .ToArray();

        TypeForm[] formtype = new TypeForm[input.Length];
        Array.Fill(formtype, TypeForm.Italic);

        TranslatedString translated = LibLouis.Instance.Translate(
            markerTables, input, 200, formtype, new string(request),
            new int[input.Length], new int[200], -1, TranslationMode.Regular);

        Assert.True(translated.Output.Length > input.Length, "expected the marker table to grow the text");
        Assert.Equal(input.Length, translated.OutputSpacing!.Length);
    }

    [Fact]
    public void Spacing_LeadingX_DisablesTheComputation()
    {
        const string input = "Anden linje, med kursiveret tekst.";

        // liblouis treats a leading 'X' as "do not compute", so there is no answer to report.
        string request = 'X' + new string('0', input.Length - 1);

        TranslatedString translated = Translate(input, request, 200);

        Assert.Null(translated.OutputSpacing);
    }

    [Fact]
    public void Spacing_BackTranslation_CoversTheWholeOutput()
    {
        // Back-translation opens with memset(spacing, '*', outlen) and then writes per output cell,
        // so the buffer has to be sized to the output capacity, not to the input. Here the capacity
        // is four times the input's length: sizing to the input overran the buffer by 80 bytes.
        const string braille = "@anç linje, m kursi#rò ükz.";

        int outputLength = braille.Length * 4;

        TranslatedString translated = BackTranslate(braille, new string('0', braille.Length), outputLength);

        Assert.True(translated.Output.Length > braille.Length, "expected back-translation to grow the text");
        Assert.Equal(translated.Output.Length, translated.OutputSpacing!.Length);
        Assert.All(translated.OutputSpacing, c => Assert.True(c == '*' || char.IsAsciiDigit(c), $"unexpected spacing value '{c}'"));
    }

    [Fact]
    public void Spacing_CollapsesSurrogatePairs_ToOneEntryPerCharacter()
    {
        // liblouis indexes the spacing buffer in widechars, so the caller's per-char request has to
        // collapse a surrogate pair into a single entry on the way in. Counting in UTF-16 units
        // would describe a longer buffer than the one allocated.
        const string input = "a\U0001F600b";

        Assert.Equal(4, input.Length);

        TranslatedString translated = Translate(input, new string('5', input.Length), 40);

        Assert.Equal(3, translated.OutputSpacing!.Length);
    }

    [Fact]
    public void Spacing_MustMatchTheInputLength()
    {
        Assert.Throws<ArgumentException>(() => Translate("Anden linje.", "00", 200));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spacing_OnTheOverloadsWithoutPositions_IsComputedAndDiscarded(bool forward)
    {
        // These overloads return a bare string and have nowhere to report the answer, but liblouis
        // still writes into the buffer, so it has to be sized the same way.
        const string input = "Anden linje, med kursiveret tekst.";

        string request = new string('0', input.Length);

        string output = forward
            ? LibLouis.Instance.Translate(Tables, input, input.Length * 4, null, request, TranslationMode.Regular)
            : LibLouis.Instance.BackTranslate(Tables, input, input.Length * 4, null, request, TranslationMode.Regular);

        Assert.NotEmpty(output);
    }
}
