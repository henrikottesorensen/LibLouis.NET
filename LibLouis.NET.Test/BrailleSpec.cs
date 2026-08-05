using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace LibLouis.NET.Test;

/// <summary>
/// One translation case from an upstream braille spec.
/// </summary>
public sealed record BrailleSpecCase(
    string SpecFile,
    int Line,
    string TableQuery,
    string? AssertMatch,
    string DisplayTable,
    string Input,
    string Expected,
    TestDirection Direction,
    bool ExpectedToFail)
{
    public override string ToString() =>
        $"{SpecFile}:{Line} {Direction} {Describe(Input)} -> {Describe(Expected)}";

    // Braille output is mostly U+28xx, which is unreadable in a test runner's output, so show the
    // code points for anything outside printable ASCII.
    private static string Describe(string value) =>
        value.All(c => c is >= ' ' and <= '~')
            ? $"\"{value}\""
            : string.Concat(value.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
}

public enum TestDirection
{
    Forward,
    Backward,
}

/// <summary>
/// How a spec entry's two values are used. The distinction matters: the backward leg of
/// bothDirections swaps input and expected (lou_checkyaml.c:900-902), while an explicit
/// backward testmode does not (lou_checkyaml.c:892-895).
/// </summary>
public enum TestMode
{
    Forward,
    Backward,
    BothDirections,
}

/// <summary>
/// Reads liblouis braille spec files.
/// </summary>
/// <remarks>
/// These files are not YAML mappings and cannot be deserialised. A single document repeats
/// <c>table</c>, <c>flags</c> and <c>tests</c> at the same level, which duplicate key handling
/// would collapse or reject. lou_checkyaml treats the file as an event stream where each key
/// mutates parser state, and <c>tests</c> executes against whatever is current
/// (tools/lou_checkyaml.c:1087-1139), so this reader does the same over YamlDotNet's IParser.
///
/// Consecutive <c>table</c> keys accumulate rather than replace: the following <c>tests</c> block
/// runs once per accumulated table. <c>flags</c> persists until the next <c>flags</c>.
///
/// Only the constructs the Danish specs actually use are supported. Anything else throws rather
/// than being skipped, so a spec using a feature this reader does not model fails loudly instead
/// of silently testing less than it appears to.
/// </remarks>
public static class BrailleSpecReader
{
    public static IReadOnlyList<BrailleSpecCase> Read(string path)
    {
        string specFile = Path.GetFileName(path);
        var cases = new List<BrailleSpecCase>();

        using var reader = new StreamReader(path);
        var parser = new Parser(reader);

        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        parser.Consume<MappingStart>();

        string displayTable = string.Empty;
        var tables = new List<(string Query, string? AssertMatch)>();
        TestMode mode = TestMode.Forward;

        // Consecutive table keys accumulate, but the first one after a tests block starts a fresh
        // set rather than adding to the one just used.
        bool tablesUsed = false;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;

            switch (key)
            {
                case "display":
                    displayTable = ReadTableValue(parser).Query;
                    break;

                case "table":
                    if (tablesUsed)
                    {
                        tables.Clear();
                        tablesUsed = false;
                    }

                    tables.Add(ReadTableValue(parser));
                    break;

                case "flags":
                    mode = ReadFlags(parser);
                    break;

                case "tests":
                    ReadTests(parser, specFile, tables, displayTable, mode, cases);
                    tablesUsed = true;
                    break;

                default:
                    throw new NotSupportedException(
                        $"{specFile}: unsupported top level key '{key}'. This reader models only the " +
                        "constructs the Danish specs use; see lou_checkyaml.c for the full format.");
            }

        }

        return cases;
    }

    /// <summary>
    /// A table value is either a file name or a query mapping. Queries are passed to lou_findTable
    /// as "key:value key:value"; __assert-match is a harness directive, not part of the query.
    /// </summary>
    private static (string Query, string? AssertMatch) ReadTableValue(IParser parser)
    {
        if (parser.Current is Scalar scalar)
        {
            parser.MoveNext();
            return (scalar.Value, null);
        }

        parser.Consume<MappingStart>();

        var terms = new List<string>();
        string? assertMatch = null;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;
            string value = parser.Consume<Scalar>().Value;

            if (key == "__assert-match")
            {
                assertMatch = value;
            }
            else
            {
                terms.Add($"{key}:{value}");
            }
        }

        parser.Consume<MappingEnd>();

        return (string.Join(' ', terms), assertMatch);
    }

    private static TestMode ReadFlags(IParser parser)
    {
        parser.Consume<MappingStart>();

        TestMode mode = TestMode.Forward;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;
            string value = parser.Consume<Scalar>().Value;

            if (key != "testmode")
            {
                throw new NotSupportedException($"unsupported flag '{key}'");
            }

            mode = ParseTestMode(value);
        }

        parser.Consume<MappingEnd>();

        return mode;
    }

    private static TestMode ParseTestMode(string value) => value switch
    {
        "forward" => TestMode.Forward,
        "backward" => TestMode.Backward,
        "bothDirections" => TestMode.BothDirections,
        _ => throw new NotSupportedException($"unsupported testmode '{value}'"),
    };

    private static void ReadTests(
        IParser parser,
        string specFile,
        List<(string Query, string? AssertMatch)> tables,
        string displayTable,
        TestMode mode,
        List<BrailleSpecCase> cases)
    {
        parser.Consume<SequenceStart>();

        while (parser.Current is not SequenceEnd)
        {
            SequenceStart entryStart = parser.Consume<SequenceStart>();
            int line = (int)entryStart.Start.Line;

            string input = Unescape(parser.Consume<Scalar>().Value);
            string expected = Unescape(parser.Consume<Scalar>().Value);

            var xfail = XFail.None;
            TestMode entryMode = mode;
            bool skip = false;

            if (parser.Current is MappingStart)
            {
                (xfail, entryMode, skip) = ReadTestOptions(parser, mode);
            }

            parser.Consume<SequenceEnd>();

            if (skip)
            {
                continue;
            }

            foreach ((string query, string? assertMatch) in tables)
            {
                // Forward compares translate(input) with expected. An explicit backward testmode
                // means the entry is already written braille-first, so it is not swapped. The
                // backward leg of bothDirections is: the expected braille is the input, and the
                // original text is what back translation should produce.
                if (entryMode is TestMode.Forward or TestMode.BothDirections)
                {
                    cases.Add(new BrailleSpecCase(
                        specFile, line, query, assertMatch, displayTable,
                        input, expected, TestDirection.Forward, xfail.HasFlag(XFail.Forward)));
                }

                if (entryMode == TestMode.Backward)
                {
                    cases.Add(new BrailleSpecCase(
                        specFile, line, query, assertMatch, displayTable,
                        input, expected, TestDirection.Backward, xfail.HasFlag(XFail.Backward)));
                }
                else if (entryMode == TestMode.BothDirections)
                {
                    cases.Add(new BrailleSpecCase(
                        specFile, line, query, assertMatch, displayTable,
                        expected, input, TestDirection.Backward, xfail.HasFlag(XFail.Backward)));
                }
            }
        }

        parser.Consume<SequenceEnd>();
    }

    [Flags]
    private enum XFail
    {
        None = 0,
        Forward = 1,
        Backward = 2,
        Both = Forward | Backward,
    }

    private static (XFail XFail, TestMode Mode, bool Skip) ReadTestOptions(
        IParser parser, TestMode mode)
    {
        parser.Consume<MappingStart>();

        var xfail = XFail.None;
        bool skip = false;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;

            switch (key)
            {
                case "xfail":
                    xfail = ReadXFail(parser);
                    break;

                case "testmode":
                    mode = ParseTestMode(parser.Consume<Scalar>().Value);
                    break;

                // Emphasis is applied through typeform, which this prototype does not drive yet.
                // Skip the value so the rest of the file still parses, and drop the case: silently
                // running it without the typeform would compare against the wrong expectation.
                case "typeform":
                    parser.SkipThisAndNestedEvents();
                    skip = true;
                    break;

                default:
                    throw new NotSupportedException($"unsupported test option '{key}'");
            }
        }

        parser.Consume<MappingEnd>();

        return (xfail, mode, skip);
    }

    /// <summary>
    /// xfail is either a scalar, where only "false" and "off" are falsy
    /// (tools/lou_checkyaml.c:379-389), or a mapping naming the failing directions.
    /// </summary>
    private static XFail ReadXFail(IParser parser)
    {
        if (parser.Current is Scalar scalar)
        {
            parser.MoveNext();
            return scalar.Value is "false" or "off" ? XFail.None : XFail.Both;
        }

        parser.Consume<MappingStart>();

        var xfail = XFail.None;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;
            string value = parser.Consume<Scalar>().Value;
            bool set = value is not ("false" or "off");

            if (set)
            {
                xfail |= key switch
                {
                    "forward" => XFail.Forward,
                    "backward" => XFail.Backward,
                    _ => throw new NotSupportedException($"unsupported xfail direction '{key}'"),
                };
            }
        }

        parser.Consume<MappingEnd>();

        return xfail;
    }

    /// <summary>
    /// The specs use single quoted scalars, where YAML performs no escape processing at all, and
    /// rely on liblouis to interpret the escapes itself. Only the forms the Danish specs actually
    /// use are handled: \xNNNN and \uNNNN code points, and \\ for a literal backslash.
    /// Without the backslash case, 'at\\bliver' parses as two backslashes and translates to two
    /// cells where upstream expects one.
    /// </summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                if (value[i + 1] is 'x' or 'y' or 'u' && i + 5 < value.Length &&
                    ushort.TryParse(
                        value.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                {
                    builder.Append((char)code);
                    i += 5;
                    continue;
                }

                if (value[i + 1] is '\\' or '"')
                {
                    builder.Append(value[i + 1]);
                    i++;
                    continue;
                }
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }
}
