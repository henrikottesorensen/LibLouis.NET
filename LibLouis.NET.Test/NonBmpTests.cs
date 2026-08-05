using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// On a UCS-4 build a liblouis widechar holds a whole Unicode character, so a non-BMP character
/// occupies one widechar but two chars of a .NET string. Passing string.Length as a widechar
/// count therefore overstates the length of the buffer.
///
/// lou_translateString survives that, because it clamps at the NUL terminator. lou_dotsToChar and
/// lou_charToDots do not clamp: they read and write exactly the count they are given
/// (lou_translateString.c:4142), so a count in the wrong unit reads past the input buffer.
/// </summary>
public class NonBmpTests
{
    /// <summary>U+1D11E MUSICAL SYMBOL G CLEF - one character, two UTF-16 code units.</summary>
    private const string NonBmp = "\U0001D11E\U0001D11E";

    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    /// <summary>
    /// How many widechars liblouis sees for <paramref name="value"/>: whole characters on a UCS-4
    /// build, UTF-16 code units on a UCS-2 one.
    /// </summary>
    private static int ExpectedCells(string value)
    {
        return NativeShim.lou_charSize() == 4
            ? value.EnumerateRunes().Count()
            : value.Length;
    }

    [Fact]
    public void CharactersToDots_ProducesOneCellPerWidecharNotPerCodeUnit()
    {
        string dots = LibLouis.Instance.CharactersToDots(TablePaths(), NonBmp);

        Assert.Equal(ExpectedCells(NonBmp), dots.Length);
    }

    [Fact]
    public void DotsToCharacters_ProducesOneCharacterPerCell()
    {
        string dots = LibLouis.Instance.CharactersToDots(TablePaths(), NonBmp);

        string roundTripped = LibLouis.Instance.DotsToCharacters(TablePaths(), dots);

        Assert.Equal(dots.Length, roundTripped.Length);
    }

    /// <summary>
    /// BMP text must keep behaving exactly as before: there string.Length and the widechar count
    /// agree, so this guards the common case against the fix.
    /// </summary>
    [Fact]
    public void CharactersToDots_IsUnchangedForBmpText()
    {
        const string input = "abc";

        string dots = LibLouis.Instance.CharactersToDots(TablePaths(), input);

        Assert.Equal(input.Length, dots.Length);
    }
}
