using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// liblouis is not thread safe and its state is process-global, so every native call in the
/// assembly has to serialise on one lock - including the ones that do not obviously touch shared
/// state.
/// </summary>
/// <remarks>
/// Holding the lock is not directly observable: lou_version returns a static string and the
/// Logging setters are single pointer-sized writes, so an unsynchronised build does not reliably
/// misbehave. These tests therefore guard the two things that are observable - that no native
/// entry point was left outside the lock, and that adding the lock did not introduce a deadlock
/// or change behaviour.
/// </remarks>
public class NativeLockTests
{
    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    [Fact]
    public void VersionIsReported()
    {
        Assert.False(string.IsNullOrWhiteSpace(LibLouis.Instance.Version));
    }

    [Fact]
    public void LogLevelRoundTrips()
    {
        LogLevel previous = Logging.LogLevel;

        try
        {
            Logging.LogLevel = LogLevel.Warning;
            Assert.Equal(LogLevel.Warning, Logging.LogLevel);
        }
        finally
        {
            Logging.LogLevel = previous;
        }
    }

    /// <summary>
    /// The lock is shared between LibLouis and the static Logging helper, and Monitor is
    /// reentrant, so hammering all three from several threads must neither deadlock nor produce a
    /// wrong translation.
    /// </summary>
    [Fact]
    public async Task ConcurrentUseDoesNotDeadlockOrCorrupt()
    {
        const string input = "Første linje";
        const string expected = "@fze linje";

        LogLevel previous = Logging.LogLevel;

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

        ConcurrentBag<string> failures = [];

        try
        {
            Task[] workers =
            [
                .. Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        string result = LibLouis.Instance.Translate(
                            TablePaths(), input, 64, null, null, TranslationMode.Regular);

                        if (result != expected)
                        {
                            failures.Add($"translation returned '{result}'");
                            return;
                        }
                    }
                })),
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        _ = LibLouis.Instance.Version;
                        Logging.LogLevel = LogLevel.Error;
                    }
                }),
            ];

            Task all = Task.WhenAll(workers);

            Assert.Same(
                all,
                await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(30))));

            await all;
        }
        finally
        {
            Logging.LogLevel = previous;
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Catches a native call added later without the lock. Deliberately source-based: there is no
    /// runtime signal for "this P/Invoke ran unsynchronised".
    /// </summary>
    /// <remarks>
    /// A call that genuinely does not need the lock has to say so, by carrying an "unlocked:"
    /// comment giving the reason. That keeps the exemptions few and explains each one, instead of
    /// letting the test quietly special-case whole methods.
    /// </remarks>
    [Theory]
    [InlineData("LibLouis.cs")]
    [InlineData("Logging.cs")]
    public void EveryNativeCallSiteIsLockedOrJustified(string fileName)
    {
        string[] lines = ReadLibrarySource(fileName).Split('\n');

        int depth = 0;
        int lockDepth = -1;
        bool pendingLock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            // The body starts at the brace on the next line, so record the depth once we are
            // actually inside it rather than on the "lock (" line itself.
            if (line.StartsWith("lock (", StringComparison.Ordinal))
            {
                pendingLock = true;
            }

            bool isNativeCall = line.Contains("NativeMethods.", StringComparison.Ordinal)
                && !line.StartsWith("//", StringComparison.Ordinal)
                && !line.StartsWith("///", StringComparison.Ordinal)
                && !line.Contains("NativeMethods.LoggingCallback", StringComparison.Ordinal);

            if (isNativeCall && lockDepth < 0)
            {
                bool justified = lines
                    .Take(i)
                    .Reverse()
                    .TakeWhile(l => l.Trim().StartsWith("//", StringComparison.Ordinal))
                    .Any(l => l.Contains("unlocked:", StringComparison.Ordinal));

                Assert.True(justified, $"{fileName}: native call outside a lock: {line}");
            }

            int updated = depth + lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');

            if (pendingLock && updated > depth)
            {
                lockDepth = updated;
                pendingLock = false;
            }

            depth = updated;

            if (lockDepth >= 0 && depth < lockDepth)
            {
                lockDepth = -1;
            }
        }
    }

    private static string ReadLibrarySource(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LibLouis.NET.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string path = Path.Combine(directory.FullName, "LibLouis.NET", fileName);

        Assert.True(File.Exists(path), $"could not locate {path}");

        return File.ReadAllText(path);
    }
}
