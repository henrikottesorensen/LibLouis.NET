using System;
using System.IO;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// liblouis owns every string it returns, so the wrapper must not hand those pointers to the
/// marshaller's Free.
///
///   * lou_setDataPath / lou_getDataPath return a pointer into a static char[MAXSTRING] inside
///     liblouis (compileTranslationTable.c:59-73). Passing that to free() is undefined behaviour
///     on every platform.
///   * lou_findTable returns malloc'd memory. Our Windows binaries are built with mingw-w64 and
///     allocate from msvcrt.dll, while .NET frees through ucrtbase.dll - different heaps, so
///     freeing it from managed code corrupts the heap there.
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
