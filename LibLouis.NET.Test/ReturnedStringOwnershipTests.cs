using System;
using System.IO;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// The strings liblouis returns must not be freed by the default marshaller.
///
///   * lou_setDataPath / lou_getDataPath return a pointer into a static char[MAXSTRING] inside
///     liblouis (compileTranslationTable.c:59-73). Passing that to free() is undefined behaviour
///     on every platform.
///   * lou_findTable returns malloc'd memory the caller frees, but with liblouis's C runtime
///     rather than the CoTaskMemFree .NET would use on Windows - see LiblouisHeapTests.
/// </summary>
public class ReturnedStringOwnershipTests
{
    /// <summary>
    /// Setting the data path returns the static buffer, which the marshaller would then free.
    /// </summary>
    /// <remarks>
    /// The path is the test output directory rather than something arbitrary, because the data
    /// path takes part in resolving relative table names and other tests rely on that.
    /// </remarks>
    [Fact]
    public void DataPath_RoundTripsWithoutFreeingLiblouisMemory()
    {
        string path = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        LibLouis.Instance.DataPath = path;

        Assert.Equal(path, LibLouis.Instance.DataPath);

        // Reading it again returns the same static buffer; a stale free shows up here as a crash
        // or as garbage.
        Assert.Equal(path, LibLouis.Instance.DataPath);
    }
}
