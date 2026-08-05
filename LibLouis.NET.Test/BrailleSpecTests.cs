using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// Runs the upstream braille specs for Danish through the managed wrapper.
/// </summary>
/// <remarks>
/// These are liblouis's own expectations, so they check the wrapper end to end against thousands of
/// cases nobody here had to invent: not just that a translation succeeds, but that it produces the
/// characters upstream says it should, in both directions.
///
/// The specs live in braille-specs/ and are copied verbatim from
/// upstream/liblouis-&lt;version&gt;/tests/braille-specs/. Re-copy them when the upstream version is
/// bumped; the diff is the set of expectations that changed.
///
/// One test per spec file rather than per case. Ten thousand xunit cases makes discovery slow and
/// buries a real regression in an unreadable log; a single failure listing every mismatch is more
/// use than ten thousand separate red entries.
/// </remarks>
public class BrailleSpecTests
{
    private static readonly string SpecDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "braille-specs");

    // tables/ is the upstream set. Nota's own tables live in nota-tables/ and no longer shadow it,
    // so the specs can be checked against the tables they were actually written for.
    private static readonly string TableDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables");

    public static TheoryData<string> SpecFiles()
    {
        var data = new TheoryData<string>();

        foreach (string path in Directory.EnumerateFiles(SpecDirectory, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SpecFiles))]
    public void MatchesUpstreamExpectations(string specFile)
    {
        IReadOnlyList<BrailleSpecCase> cases = BrailleSpecReader.Read(Path.Combine(SpecDirectory, specFile));

        IndexUpstreamTables();

        var mismatches = new List<string>();
        var unexpectedPasses = new List<string>();
        int checkedCount = 0;

        foreach (BrailleSpecCase testCase in cases)
        {

            string table = ResolveTable(testCase, TableCache.Value);
            string? actual = Run(testCase, table);
            bool matched = actual == testCase.Expected;
            checkedCount++;

            if (testCase.ExpectedToFail)
            {
                // Upstream reports an unexpected pass as a warning rather than an error: the case
                // is known-broken, and it passing usually means the expectation moved.
                if (matched)
                {
                    unexpectedPasses.Add(testCase.ToString());
                }
            }
            else if (!matched)
            {
                mismatches.Add($"{testCase}\n      actual: {Describe(actual)}");
            }
        }


        Assert.True(
            mismatches.Count == 0,
            $"{specFile}: {mismatches.Count} of {checkedCount} cases did not match upstream " +
            $"({unexpectedPasses.Count} xfail cases passed unexpectedly).\n  " +
            string.Join("\n  ", mismatches.Take(25)) +
            (mismatches.Count > 25 ? $"\n  ... and {mismatches.Count - 25} more" : string.Empty));
    }

    private static string? Run(BrailleSpecCase testCase, string table)
    {
        string[] tables = [ResolveDisplayTable(testCase.DisplayTable), table];
        int outputLength = Math.Max(testCase.Input.Length, testCase.Expected.Length) * 4;

        try
        {
            return testCase.Direction == TestDirection.Forward
                ? LibLouis.Instance.Translate(tables, testCase.Input, outputLength, null, null, TranslationMode.Regular)
                : LibLouis.Instance.BackTranslate(tables, testCase.Input, outputLength, null, null, TranslationMode.Regular);
        }
        catch (LibLouisException ex)
        {
            // A translation that fails outright is a mismatch, not an error: upstream marks some of
            // these xfail, and letting it throw would stop the whole file at the first one. The
            // message is carried into the comparison so a failure says why, rather than only that
            // nothing came back.
            return $"<translation failed: {ex.Message}>";
        }
    }

    // Every query from every spec is resolved once, up front, before any translation runs.
    // lou_findTable's return value is freed by the marshaller with the wrong allocator (P/Invoke
    // audit item 3), so interleaving these calls with translations corrupts the native heap.
    private static readonly Lazy<Dictionary<string, string>> TableCache = new(() =>
    {
        LibLouis.Instance.IndexTables(
            Directory.EnumerateFiles(TableDirectory)
                .Where(f => Path.GetExtension(f) is ".ctb" or ".utb" or ".uti" or ".dis" or ".cti" or ".dic"));

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string query in Directory.EnumerateFiles(SpecDirectory, "*.yaml")
                     .SelectMany(BrailleSpecReader.Read)
                     .Select(c => c.TableQuery)
                     .Distinct(StringComparer.Ordinal))
        {
            resolved[query] = LibLouis.Instance.FindTable(query) ?? string.Empty;
        }

        return resolved;
    });

    private static void IndexUpstreamTables() => _ = TableCache.Value;

    /// <summary>
    /// Resolving a table query is what lou_findTable does, and the specs assert which file a query
    /// should select, so this covers table resolution as well as translation.
    /// </summary>
    private static string ResolveTable(BrailleSpecCase testCase, Dictionary<string, string> cache)
    {
        if (!cache.TryGetValue(testCase.TableQuery, out string? resolved))
        {
            resolved = LibLouis.Instance.FindTable(testCase.TableQuery) ?? string.Empty;
            cache[testCase.TableQuery] = resolved;
        }

        Assert.False(
            string.IsNullOrEmpty(resolved),
            $"No table matched the query '{testCase.TableQuery}' from {testCase.SpecFile}:{testCase.Line}");

        if (testCase.AssertMatch is not null)
        {
            Assert.True(
                string.Equals(Path.GetFileName(resolved), testCase.AssertMatch, StringComparison.Ordinal),
                $"Query '{testCase.TableQuery}' resolved to {Path.GetFileName(resolved)}, " +
                $"but {testCase.SpecFile}:{testCase.Line} asserts {testCase.AssertMatch}");
        }

        return resolved;
    }

    /// <summary>
    /// A spec's display table is usually a file name, but it can also be an inline table written as
    /// a YAML block scalar, for instance to include the standard one and then override a character.
    /// liblouis only takes paths, so the inline form is written out next to the tables, where the
    /// includes inside it resolve.
    /// </summary>
    private static string ResolveDisplayTable(string display)
    {
        // A file name never contains a newline, so this distinguishes the two forms.
        if (!display.Contains('\n', StringComparison.Ordinal))
        {
            return Path.Combine(TableDirectory, display);
        }

        string name = $"inline-{Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(display)))[..8]}.dis";
        string path = Path.Combine(TableDirectory, name);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, display);
        }

        return path;
    }

    private static string Describe(string? value) =>
        value is null
            ? "<translation failed>"
            : string.Concat(value.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
}
