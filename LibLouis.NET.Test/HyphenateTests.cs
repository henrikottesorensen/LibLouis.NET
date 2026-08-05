using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// lou_hyphenate takes a caller-allocated <c>char *hyphens</c> buffer and writes inlen + 1 bytes
/// into it: '0' or '1' per character, plus a terminator (lou_translateString.c:4080).
/// </summary>
public class HyphenateTests
{
    private const string Word = "bogstaver";

    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nota-tables", t))];

    [Fact]
    public void Hyphenate_ReturnsOneHyphenationFlagPerCharacter()
    {
        string hyphens = LibLouis.Instance.Hyphenate(TablePaths(), Word, TranslationMode.Regular);

        Assert.Equal(Word.Length, hyphens.Length);
        Assert.Matches("^[012]+$", hyphens);

        // "bog-sta-ver": the table has to find at least one break, otherwise this test is not
        // exercising hyphenation at all.
        Assert.Contains('1', hyphens);
    }

    /// <summary>
    /// The result must describe the word that was passed in, not a NUL terminator the wrapper
    /// added. Unlike lou_translateString, lou_hyphenate does not clamp inlen at the first NUL:
    /// it memcpy's exactly inlen characters, so an inflated inlen hyphenates the terminator too.
    /// </summary>
    [Fact]
    public void Hyphenate_DoesNotIncludeTheNulTerminator()
    {
        foreach (string word in new[] { "a", "bo", "bogstaver", "hyphenation" })
        {
            string hyphens = LibLouis.Instance.Hyphenate(TablePaths(), word, TranslationMode.Regular);

            Assert.Equal(word.Length, hyphens.Length);
        }
    }

    /// <summary>
    /// inlen is a widechar count. On a UCS-4 build a non-BMP character is one widechar but two
    /// chars, so passing string.Length claims the buffer is longer than it is - and lou_hyphenate
    /// memcpy's exactly inlen widechars out of it, with no terminator to stop at.
    /// </summary>
    [Theory]
    [InlineData("bogstaver\U0001D11E")]              // one flag too many
    [InlineData("bogstaver\U0001D11E\U0001D11E")]    // and reads past the input buffer
    public void Hyphenate_ReturnsOneFlagPerWidecharNotPerCodeUnit(string word)
    {
        int expected = SafeNativeMethods.lou_charSize() == 4
            ? word.EnumerateRunes().Count()
            : word.Length;

        string hyphens = LibLouis.Instance.Hyphenate(TablePaths(), word, TranslationMode.Regular);

        Assert.Equal(expected, hyphens.Length);
    }
}
