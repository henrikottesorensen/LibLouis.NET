using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// liblouis writes the typeform array back for every *output* cell, not for every input
/// character. Passing a typeform array sized to the input therefore lets native code write
/// past the end of a managed array whenever the translation grows the text - which the
/// marker tables in this repository do routinely.
/// </summary>
public class TypeFormBufferTests
{
    private const string Input = "This is a test.";

    /// <summary>Translation of <see cref="Input"/> with the marker tables, 20 cells for 15 characters.</summary>
    private const string ExpectedOutput = "`,@this is a test.`,";

    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g16-markers.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    /// <summary>
    /// Documents the native contract that makes the overrun possible, independent of the wrapper:
    /// lou_translateString writes one typeform entry per output cell. Uses a deliberately
    /// oversized buffer so nothing is corrupted while we measure how far native code writes.
    /// </summary>
    [Fact]
    public void Native_WritesOneTypeformEntryPerOutputCell()
    {
        int charSize = SafeNativeMethods.lou_charSize();
        Encoding encoder = charSize == 4 ? Encoding.UTF32 : Encoding.Unicode;

        int inputLength = Input.Length;
        int outputLength = ExpectedOutput.Length;

        byte[] inputBuffer = encoder.GetBytes(Input + "\0");
        byte[] outputBuffer = new byte[(outputLength + 1) * charSize];

        // Far larger than either length, so native writes stay in bounds and are observable.
        // typeform is in/out: the first inputLength entries are real input (foreign language,
        // matching the other tests), the rest are plain text. Neither value collides with the
        // ASCII '0' / '8' that liblouis writes back, so any such entry marks a native write.
        ushort[] typeform = new ushort[outputLength * 4];
        Array.Fill(typeform, (ushort)TypeForm.ForeignLanguage, 0, inputLength);

        int inLen = inputLength;
        int outLen = outputLength;

        int result = SafeNativeMethods.lou_translateString(
            SafeNativeMethods.Utf8(string.Join(',', TablePaths())),
            inputBuffer,
            ref inLen,
            outputBuffer,
            ref outLen,
            typeform,
            null,
            0);

        Assert.NotEqual(0, result);
        Assert.Equal(ExpectedOutput, encoder.GetString(outputBuffer, 0, outLen * charSize));

        // liblouis writes the ASCII characters '0' / '8' per output cell.
        int lastWritten = Array.FindLastIndex(typeform, t => t == '0' || t == '8');

        Assert.Equal(outLen - 1, lastWritten);

        // The point of the test: native wrote beyond the input length, so an input-sized
        // managed array would have been overrun by exactly this many entries.
        Assert.True(
            lastWritten >= inputLength,
            $"Expected native writes past input length {inputLength}, but last write was at {lastWritten}.");
    }

    /// <summary>
    /// The wrapper must not let native code write into - let alone past - the caller's typeform
    /// array. The public contract sizes typeform to the input, so the wrapper owes the caller a
    /// buffer big enough for the output.
    /// </summary>
    [Fact]
    public void Translate_DoesNotWriteIntoCallersTypeformArray()
    {
        TypeForm[] typeform = new TypeForm[Input.Length];
        Array.Fill(typeform, TypeForm.ForeignLanguage);

        TypeForm[] untouched = (TypeForm[])typeform.Clone();

        string output = LibLouis.Instance.Translate(
            TablePaths(), Input, Input.Length * 2, typeform, null, TranslationMode.Regular);

        Assert.Equal(ExpectedOutput, output);
        Assert.Equal(untouched, typeform);
    }

    /// <summary>
    /// The same overrun through the position-reporting overload.
    /// </summary>
    [Fact]
    public void TranslateWithPositions_DoesNotWriteIntoCallersTypeformArray()
    {
        int outputLength = Input.Length * 2;

        TypeForm[] typeform = new TypeForm[Input.Length];
        Array.Fill(typeform, TypeForm.ForeignLanguage);

        TypeForm[] untouched = (TypeForm[])typeform.Clone();

        TranslatedString translated = LibLouis.Instance.Translate(
            TablePaths(),
            Input,
            outputLength,
            typeform,
            null,
            new int[Input.Length],
            new int[outputLength],
            0,
            TranslationMode.Regular);

        Assert.Equal(ExpectedOutput, translated.Output);
        Assert.Equal(untouched, typeform);
    }

}
