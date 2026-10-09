using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Xunit;

// These tests exercise the spacing overloads, which are obsolete since liblouis 3.39.0 deprecated
// the parameter but stay supported until they are removed.
#pragma warning disable CS0618

namespace LibLouis.NET.Test;

/// <summary>
/// liblouis 3.39.0 deprecated the spacing parameter and ignores it, logging a warning whenever one
/// is passed. The obsolete overloads still accept it, so existing callers keep compiling, but the
/// wrapper drops it rather than handing liblouis a buffer it will never fill.
/// </summary>
public class SpacingTests
{
    private const string Input = "Anden linje, med kursiveret tekst.";

    private const string Braille = "@anç linje, m kursi#rò ükz.";

    private static readonly string[] Tables =
        new[] { "da-dk-braillo.dis", "da-dk-g26.ctb" }
            .Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))
            .ToArray();

    [Fact]
    public void Translate_WithSpacing_MatchesTheOverloadWithout()
    {
        TranslatedString with = LibLouis.Instance.Translate(
            Tables, Input, 200, null, new string('3', Input.Length),
            new int[Input.Length], new int[200], -1, TranslationMode.Regular);
        TranslatedString without = LibLouis.Instance.Translate(
            Tables, Input, 200, null,
            new int[Input.Length], new int[200], -1, TranslationMode.Regular);

        Assert.Equal(without.Output, with.Output);
        Assert.Equal(without.InputPosition, with.InputPosition);
        Assert.Equal(without.OutputPosition, with.OutputPosition);
    }

    [Fact]
    public void BackTranslate_WithSpacing_MatchesTheOverloadWithout()
    {
        TranslatedString with = LibLouis.Instance.BackTranslate(
            Tables, Braille, 200, null, new string('0', Braille.Length),
            new int[Braille.Length], new int[200], -1, TranslationMode.Regular);
        TranslatedString without = LibLouis.Instance.BackTranslate(
            Tables, Braille, 200, null,
            new int[Braille.Length], new int[200], -1, TranslationMode.Regular);

        Assert.Equal(without.Output, with.Output);
        Assert.Equal(without.InputPosition, with.InputPosition);
        Assert.Equal(without.OutputPosition, with.OutputPosition);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StringOverloads_WithSpacing_MatchTheOverloadsWithout(bool forward)
    {
        string input = forward ? Input : Braille;
        string request = new('0', input.Length);

        string with = forward
            ? LibLouis.Instance.Translate(Tables, input, 200, null, request, TranslationMode.Regular)
            : LibLouis.Instance.BackTranslate(Tables, input, 200, null, request, TranslationMode.Regular);
        string without = forward
            ? LibLouis.Instance.Translate(Tables, input, 200, null, TranslationMode.Regular)
            : LibLouis.Instance.BackTranslate(Tables, input, 200, null, TranslationMode.Regular);

        Assert.Equal(without, with);
    }

    [Fact]
    public void Spacing_IsNotValidated_SinceItIsIgnored()
    {
        // The length check went with the buffer: there is nothing left for a mismatch to break.
        string output = LibLouis.Instance.Translate(Tables, Input, 200, null, "00", TranslationMode.Regular);

        Assert.NotEmpty(output);
    }

    /// <summary>
    /// liblouis logs "the spacing parameter is deprecated and ignored" for every call that passes
    /// one. The wrapper never does, so a caller still on the obsolete overloads does not get a
    /// warning per translation in their logs.
    /// </summary>
    [Fact]
    public void Spacing_IsNotPassedOn_SoLiblouisDoesNotWarn()
    {
        CollectingLogger logger = new();
        ILogger previousLogger = LibLouis.Instance.Logger;
        LogLevel previousLevel = Logging.LogLevel;
        LibLouis.Instance.Logger = logger;
        Logging.LogLevel = LogLevel.All;

        try
        {
            LibLouis.Instance.Translate(Tables, Input, 200, null, new string('0', Input.Length), TranslationMode.Regular);
            LibLouis.Instance.BackTranslate(Tables, Braille, 200, null, new string('0', Braille.Length), TranslationMode.Regular);

            // Prove the logger is actually hearing from liblouis, so the assertion below is not
            // vacuously true.
            Assert.Throws<LibLouisException>(
                () => LibLouis.Instance.Translate(["no-such-table-at-all.ctb"], "x", 8, null, TranslationMode.Regular));
        }
        finally
        {
            Logging.LogLevel = previousLevel;
            LibLouis.Instance.Logger = previousLogger;
        }

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("spacing", StringComparison.OrdinalIgnoreCase));
    }
}
