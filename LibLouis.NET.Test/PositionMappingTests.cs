using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// liblouis indexes its position arrays in widechars - whole Unicode characters on our UCS-4
/// builds. .NET callers read them as indices into a string, which is UTF-16. The two agree for
/// BMP text and diverge on the first non-BMP character, so the wrapper translates them.
/// </summary>
/// <remarks>
/// The invariant that matters is not "the numbers look right" but that every value is directly
/// usable as a string index: <c>Output[result.InputPosition[k]]</c> must address the character
/// liblouis meant, and must never land on the trailing half of a surrogate pair.
/// </remarks>
public class PositionMappingTests
{
    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    private static TranslatedString Translate(string input)
    {
        int outputLength = Math.Max(16, input.Length * 4);

        return LibLouis.Instance.Translate(
            TablePaths(),
            input,
            outputLength,
            null,
            null,
            new int[input.Length],
            new int[outputLength],
            0,
            TranslationMode.Regular);
    }

    /// <summary>
    /// Sized to the strings they index, so no slicing guesswork is needed.
    /// </summary>
    [Theory]
    [InlineData("Første linje. Anden linje.")]
    [InlineData("bogstaver")]
    [InlineData("a\U0001D11Eb")]
    [InlineData("\U0001D11E\U0001D11E")]
    [InlineData("😀 hej")]
    public void ArraysAreSizedToTheStringsTheyIndex(string input)
    {
        TranslatedString result = Translate(input);

        Assert.Equal(input.Length, result.OutputPosition.Length);
        Assert.Equal(result.Output.Length, result.InputPosition.Length);
    }

    /// <summary>
    /// Every InputPosition value must be a usable index into the input string, and must address
    /// the start of a character rather than the low half of a surrogate pair.
    /// </summary>
    [Theory]
    [InlineData("Første linje. Anden linje.")]
    [InlineData("a\U0001D11Eb")]
    [InlineData("\U0001D11E\U0001D11E")]
    [InlineData("😀 hej")]
    public void InputPositionsAddressWholeCharactersOfTheInput(string input)
    {
        TranslatedString result = Translate(input);

        foreach (int position in result.InputPosition)
        {
            Assert.InRange(position, 0, input.Length - 1);
            Assert.False(
                char.IsLowSurrogate(input[position]),
                $"position {position} lands on the trailing half of a surrogate pair");
        }
    }

    /// <summary>
    /// The same, in the other direction.
    /// </summary>
    [Theory]
    [InlineData("Første linje. Anden linje.")]
    [InlineData("a\U0001D11Eb")]
    [InlineData("😀 hej")]
    public void OutputPositionsAddressWholeCharactersOfTheOutput(string input)
    {
        TranslatedString result = Translate(input);

        foreach (int position in result.OutputPosition)
        {
            Assert.InRange(position, 0, result.Output.Length - 1);
            Assert.False(
                char.IsLowSurrogate(result.Output[position]),
                $"position {position} lands on the trailing half of a surrogate pair");
        }
    }

    /// <summary>
    /// Both halves of a surrogate pair belong to the same character, so both must report the same
    /// braille cell.
    /// </summary>
    [Fact]
    public void SurrogatePairHalvesShareAnOutputPosition()
    {
        const string input = "a\U0001D11Eb";

        TranslatedString result = Translate(input);

        // index 1 and 2 are the two halves of U+1D11E
        Assert.Equal(result.OutputPosition[1], result.OutputPosition[2]);

        // and the surrounding BMP characters map elsewhere
        Assert.NotEqual(result.OutputPosition[0], result.OutputPosition[1]);
    }

    /// <summary>
    /// BMP text must be completely unaffected: widechar and UTF-16 indices coincide there, so the
    /// values have to match what liblouis wrote into the caller's scratch array.
    /// </summary>
    [Fact]
    public void BmpTextIsUnchanged()
    {
        const string input = "Første linje. Anden linje, med kursiveret tekst. Tredje linje.";

        int outputLength = input.Length * 4;

        int[] scratchOutput = new int[input.Length];
        int[] scratchInput = new int[outputLength];

        TranslatedString result = LibLouis.Instance.Translate(
            TablePaths(), input, outputLength, null, null, scratchOutput, scratchInput, 0, TranslationMode.Regular);

        Assert.Equal(scratchOutput, result.OutputPosition);
        Assert.Equal(scratchInput[..result.Output.Length], result.InputPosition);
    }

    /// <summary>
    /// The pattern that consumer code actually uses, which was correct only for BMP text.
    /// </summary>
    [Theory]
    [InlineData("Første linje")]
    [InlineData("a\U0001D11Eb")]
    public void ConsumerSlicePatternStaysInBounds(string input)
    {
        TranslatedString result = Translate(input);

        int[] sliced = result.InputPosition[..result.Output.Length];

        Assert.Equal(result.InputPosition.Length, sliced.Length);
        Assert.All(sliced, p => Assert.InRange(p, 0, input.Length - 1));
    }

    /// <summary>
    /// The cursor comes back as an index into the braille output, so it has to be translated too.
    /// </summary>
    [Fact]
    public void CursorPositionIsAnIndexIntoTheOutput()
    {
        const string input = "a\U0001D11Ebc";

        int outputLength = input.Length * 4;

        // Cursor on 'b', which sits after the surrogate pair.
        TranslatedString result = LibLouis.Instance.Translate(
            TablePaths(),
            input,
            outputLength,
            null,
            null,
            new int[input.Length],
            new int[outputLength],
            input.IndexOf('b', StringComparison.Ordinal),
            TranslationMode.Regular);

        Assert.InRange(result.CursorPosition, 0, result.Output.Length - 1);
        Assert.False(char.IsLowSurrogate(result.Output[result.CursorPosition]));
    }
}
