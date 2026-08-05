using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// lou_indexTables walks its argument until it hits a NULL pointer
/// (<c>for (table = tables; *table; table++)</c>, metadata.c:905). A managed string[] marshals to
/// exactly Length pointers with no terminator, so liblouis reads past the end of the array.
/// </summary>
public class IndexTablesTests
{
    private static readonly string[] Tables = ["da-dk-g26.ctb", "da-dk-g16-markers.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nota-tables", t))];

    /// <summary>
    /// liblouis logs one "Analyzing table &lt;name&gt;" line per array entry it walks, so the number
    /// of those lines is a direct measure of how far it read.
    /// </summary>
    /// <remarks>
    /// Without the terminator this does not fail, it hangs: liblouis reads the managed memory
    /// following the array as a char* and _lou_logMessage formats it with %s until it runs out of
    /// readable memory. A regression here shows up as a test run that never finishes.
    /// </remarks>
    [Fact]
    public void IndexTables_DoesNotReadPastTheEndOfTheArray()
    {
        string[] paths = TablePaths();

        CollectingLogger logger = new();
        ILogger previous = LibLouis.Instance.Logger;
        LibLouis.Instance.Logger = logger;

        try
        {
            LibLouis.Instance.IndexTables(paths);
        }
        finally
        {
            LibLouis.Instance.Logger = previous;
        }

        List<string> analyzed = [.. logger.Messages
            .Where(m => m.StartsWith("Analyzing table ", StringComparison.Ordinal))
            .Select(m => m["Analyzing table ".Length..])];

        Assert.Equal(paths, analyzed);
    }

}
