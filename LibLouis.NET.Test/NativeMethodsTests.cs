using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

public class NativeMethodsTests
{
    [Fact]
    public void SingleMode()
    {
        string cwd = Directory.GetCurrentDirectory();

        string[] tables = ["da-dk-braillo.dis", "da-dk-g16-markers.ctb"];

        string input = "This is a test.";

        int outputLength = input.Length * 2;

        TypeForm[] modes = new TypeForm[input.Length];
        Array.Fill(modes, TypeForm.ForeignLanguage);

        string resultString = LibLouis.Instance.Translate(
            tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t)),
            input,
            outputLength,
            modes,
            null,
            TranslationMode.Regular);

        Assert.Equal("`,@this is a test.`,", resultString);
    }

    [Fact]
    public void NestedMode_EmphasisInForeignLanguage()
    {
        string[] tables = ["da-dk-braillo.dis", "da-dk-g16-markers.ctb"];

        string input = "This is a test.";
        int inputLength = input.Length;
        int outputLength = inputLength * 2;

        TypeForm[] modes = new TypeForm[inputLength];
        Array.Fill(modes, TypeForm.ForeignLanguage);
        modes[5] = TypeForm.ForeignLanguage | TypeForm.Emphasis;
        modes[6] = TypeForm.ForeignLanguage | TypeForm.Emphasis;

        string resultString = LibLouis.Instance.Translate(
            tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t)),
            input,
            outputLength,
            modes,
            null,
            TranslationMode.Regular);

        Assert.Equal("`,@this \\is\\ a test.`,", resultString);
    }

    [Fact]
    public void NestedMode_ForeignLanguageInEmphasis()
    {
        string[] tables = ["da-dk-braillo.dis", "da-dk-g16-markers.ctb"];

        string input = "Han sagde yes.";

        int inputLength = input.Length;
        int outputLength = inputLength * 2;

        TypeForm[] modes =
        [
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.Italic,
            TypeForm.ForeignLanguage | TypeForm.Italic,
            TypeForm.ForeignLanguage | TypeForm.Italic,
            TypeForm.ForeignLanguage | TypeForm.Italic,
            TypeForm.Italic
        ];

        Assert.Equal(inputLength, modes.Length);

        string resultString = LibLouis.Instance.Translate(
            tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t)),
            input,
            outputLength,
            modes,
            null,
            TranslationMode.Regular);

        Assert.Equal("\\@han sagde `,yes`,.\\", resultString);
    }

    [Fact]
    public void TestPositionResults()
    {
        const string input = "Første linje. Anden linje, med kursiveret tekst. Tredje linje.";
        const string expected = "@fze linje. @anç linje, m kursi#rò ükz. @tàdje linje.";

        string[] tables = ["tables/da-dk-braillo.dis", "tables/da-dk-g26.ctb"];

        int outputLength = input.Length * 4;
        int cursorPosition = 0;
        int[] inputPosition = new int[outputLength];
        int[] outputPosition = new int[input.Length];

        TranslatedString translated = LibLouis.Instance.Translate(tables, input, outputLength, null, null, outputPosition, inputPosition, cursorPosition, TranslationMode.Regular);

        Assert.Equal(expected, translated.Output);

        Assert.Equal(inputPosition, translated.InputPosition);
        Assert.Equal(outputPosition, translated.OutputPosition);

        Assert.Equal('A', input[inputPosition[12]]);
        Assert.Equal('A', input[inputPosition[13]]);
        Assert.Equal('n', input[inputPosition[14]]);

        Assert.Equal('@', expected[outputPosition[0]]);
        Assert.Equal(' ', expected[outputPosition[6]]);
        Assert.Equal('@', expected[outputPosition[14]]);
        Assert.Equal('n', expected[outputPosition[15]]);
    }
}
