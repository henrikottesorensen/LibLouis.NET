using System;
using System.IO;
using System.Linq;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// On a successful forward translation liblouis reports, per output cell, whether the cell
/// contains dot 7 or dot 8 (lou_translateString.c:1330). It writes that into the typeform
/// buffer - which is why the buffer has to be output-sized, and why the caller's input-sized
/// array must not receive it. The wrapper surfaces the information on TranslatedString instead,
/// so callers get it without the write-past-the-end hazard.
/// </summary>
public class OutputDotsTests
{
    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g08.ctb"];

    private static string[] EightDotTables() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    private static TranslatedString TranslateWithTypeForm(string input)
    {
        TypeForm[] typeform = new TypeForm[input.Length];

        return LibLouis.Instance.Translate(
            EightDotTables(),
            input,
            input.Length * 4,
            typeform,
            null,
            new int[input.Length],
            new int[input.Length * 4],
            0,
            TranslationMode.Regular);
    }

    /// <summary>
    /// Danish 8-dot braille marks a capital with dot 7 on the letter's own cell, so casing gives
    /// a per-cell pattern we can predict: the flag must differ between the capital and the small
    /// letters.
    /// </summary>
    [Fact]
    public void ReportsDot7OnCapitalCells()
    {
        TranslatedString result = TranslateWithTypeForm("Abc");

        Assert.NotNull(result.OutputDots78);
        Assert.Equal(result.Output.Length, result.OutputDots78.Length);

        Assert.True(result.OutputDots78[0], "capital A should carry dot 7 in an 8-dot table");
        Assert.All(result.OutputDots78.Skip(1), d => Assert.False(d, "small letters should not"));
    }

    /// <summary>
    /// liblouis only computes the information when a typeform buffer is supplied, so without one
    /// the property must be null rather than a fabricated all-false array.
    /// </summary>
    [Fact]
    public void IsNullWhenNoTypeFormWasPassed()
    {
        TranslatedString result = LibLouis.Instance.Translate(
            EightDotTables(),
            "Abc",
            16,
            null,
            null,
            new int[3],
            new int[16],
            0,
            TranslationMode.Regular);

        Assert.Null(result.OutputDots78);
    }

    /// <summary>
    /// The safety half of the contract, restated from the caller's side: surfacing the output
    /// information must not bring back the write-back into the caller's array.
    /// </summary>
    [Fact]
    public void CallersArrayStaysUntouched()
    {
        TypeForm[] typeform = new TypeForm[3];
        Array.Fill(typeform, TypeForm.Italic);

        TranslatedString result = LibLouis.Instance.Translate(
            EightDotTables(),
            "Abc",
            16,
            typeform,
            null,
            new int[3],
            new int[16],
            0,
            TranslationMode.Regular);

        Assert.NotNull(result.OutputDots78);
        Assert.All(typeform, t => Assert.Equal(TypeForm.Italic, t));
    }
}
