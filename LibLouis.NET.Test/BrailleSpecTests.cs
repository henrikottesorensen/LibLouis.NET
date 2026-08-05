using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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
        // Run on a thread with a generous stack, because compiling a table can recurse deeply.
        // ancient-languages-borger.utb needs somewhere between 640KB and 768KB to compile, which is
        // more than the test host hands a test, and a stack overflow kills the process rather than
        // failing the test.
        //
        // It is compilation, not translation: once a table list is compiled, translating through it
        // runs in 128KB. The first call using a table list is simply the one that pays for the
        // compile. Nothing about the input matters - ASCII overflows the same as non-BMP - and
        // liblouis caches compiled tables process-wide, so without a large stack somewhere the
        // result depends on which test happened to compile a given table first.
        Exception? failure = null;
        var worker = new Thread(
            () =>
            {
                try
                {
                    RunSpec(specFile);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            64 * 1024 * 1024);

        worker.Start();
        worker.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.Message);
        }
    }

    private static void RunSpec(string specFile)
    {
        BrailleSpec spec = BrailleSpecReader.Read(Path.Combine(SpecDirectory, specFile));

        IndexUpstreamTables();

        var mismatches = new List<string>();
        var unexpectedPasses = new List<string>();
        int checkedCount = 0;

        foreach (BrailleSpecCase testCase in spec.Cases)
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

        // A spec this reader cannot parse is skipped here rather than allowed to throw: the cache is
        // shared by every spec's test, so one unparseable file would otherwise fail all of them
        // instead of just its own.
        var queries = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(SpecDirectory, "*.yaml"))
        {
            try
            {
                foreach (BrailleSpecCase testCase in BrailleSpecReader.Read(file).Cases)
                {
                    if (testCase.TableQuery.Contains(':', StringComparison.Ordinal))
                    {
                        queries.Add(testCase.TableQuery);
                    }
                }
            }
            catch (Exception)
            {
                // Reported by that spec's own test.
            }
        }

        foreach (string query in queries)
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
        // A table is given three ways: as a query for lou_findTable, as a plain file name, or as an
        // inline table written as a block scalar. Inline content is the only one containing a
        // newline; a query is always key:value pairs, so a colon separates the other two.
        if (testCase.TableQuery.Contains('\n', StringComparison.Ordinal))
        {
            return Materialize(testCase.TableQuery, ".utb");
        }

        if (!testCase.TableQuery.Contains(':', StringComparison.Ordinal))
        {
            return Path.Combine(TableDirectory, testCase.TableQuery);
        }

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
    private static string ResolveDisplayTable(string display) =>
        display.Contains('\n', StringComparison.Ordinal)
            ? Materialize(display, ".dis")
            : Path.Combine(TableDirectory, display);

    /// <summary>
    /// Writes an inline table out next to the tables, where the include lines inside it resolve.
    /// liblouis only takes paths, and specs may define a display or translation table inline as a
    /// block scalar rather than naming a file — usually to include a standard table and override a
    /// rule or two.
    /// </summary>
    private static string Materialize(string content, string extension)
    {
        string hash = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(content)))[..8];
        string path = Path.Combine(TableDirectory, $"inline-{hash}{extension}");

        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
        }

        return path;
    }

    private static string Describe(string? value) =>
        value is null
            ? "<translation failed>"
            : string.Concat(value.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
}
