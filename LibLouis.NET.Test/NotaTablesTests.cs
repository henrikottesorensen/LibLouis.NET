using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// Guards the nota-tables/ directory, which the tests use in place of the upstream set.
/// </summary>
public class NotaTablesTests
{
    private static readonly string NotaTableDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nota-tables");

    private static readonly string UpstreamTableDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables");

    /// <summary>
    /// liblouis resolves an include relative to the directory of the table doing the including, so
    /// every table nota-tables/ pulls in has to be there too. Most are Nota's own; the handful of
    /// general upstream tables they need are copied in by an explicit list in the csproj, and an
    /// explicit list is exactly the kind of thing that goes stale the moment a table gains an
    /// include.
    /// </summary>
    [Fact]
    public void NotaTablesAreSelfContained()
    {
        var present = TableFilesIn(NotaTableDirectory).ToHashSet(StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string file in present)
        {
            foreach (string included in IncludesOf(Path.Combine(NotaTableDirectory, file)))
            {
                if (!present.Contains(included))
                {
                    missing.Add($"{included} (included by {file})");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "nota-tables/ is missing tables it includes, so those cannot be compiled from that " +
            "directory alone. Add them to the upstream copy list in the csproj:\n  " +
            string.Join("\n  ", missing));
    }

    /// <summary>
    /// The separate directory exists so that nothing of Nota's shadows the upstream set. Sharing a
    /// file name across the two directories is expected and fine - twenty two of Nota's tables are
    /// forks of an upstream table of the same name. What must never happen is Nota's tables being
    /// copied into tables/ as well, which is what the old single directory did.
    ///
    /// The tables that exist nowhere upstream are the reliable canary: if one of them turns up in
    /// tables/, the copy is misconfigured, whatever the file names say.
    /// </summary>
    [Fact]
    public void NotaTablesDoNotLeakIntoTheUpstreamDirectory()
    {
        Assert.True(Directory.Exists(UpstreamTableDirectory), "the upstream tables were not staged");

        string[] notaOnly =
        [
            "da-dk-g16-markers.ctb",
            "da-dk-g16_1993-markers.ctb",
            "da-dk-braillo.dis",
            "da-dk-g16-crossword.ctb",
            "da-dk-g26l.ctb",
            "da-dk-g26l-lit.ctb",
            "da-dk-g28l.ctb",
            "da-dk-g2_1993.dic",
        ];

        var upstream = TableFilesIn(UpstreamTableDirectory).ToHashSet(StringComparer.Ordinal);
        var leaked = notaOnly.Where(upstream.Contains).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(
            leaked.Count == 0,
            "tables/ should hold only the upstream set, but these tables of Nota's are in it, so " +
            "the two are being copied to the same place again:\n  " + string.Join("\n  ", leaked));
    }

    private static IEnumerable<string> TableFilesIn(string directory) =>
        Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .Where(f => f is not null && !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))!;

    private static IEnumerable<string> IncludesOf(string path) =>
        File.ReadLines(path)
            .Select(line => Regex.Match(line, @"^\s*include\s+(\S+)"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value);
}
