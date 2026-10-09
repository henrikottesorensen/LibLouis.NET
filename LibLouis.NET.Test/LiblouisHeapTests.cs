using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// lou_findTable's result is the caller's to free, with liblouis's own C runtime. On Windows that
/// is found through liblouis's import table, so the table walk is tested here against a synthetic
/// image on every platform, and against the real liblouis.dll on Windows.
/// </summary>
public unsafe class LiblouisHeapTests
{
    private static readonly string[] Tables = ["da-dk-g26.ctb", "da-dk-g16-markers.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    // RVAs in the synthetic image.
    private const int ImportTable = 0x200;
    private const int NameThunks = 0x300;
    private const int AddressThunks = 0x400;
    private const int MallocName = 0x500;
    private const int FreeName = 0x520;
    private const int LibraryName = 0x540;

    /// <summary>
    /// A minimal mapped PE image importing, from one library: an ordinal, malloc and free, bound to
    /// 0x1111, 0x2222 and 0x3333.
    /// </summary>
    private static byte[] SyntheticImage()
    {
        byte[] image = new byte[0x600];
        Span<byte> span = image;

        BitConverter.TryWriteBytes(span[0x00..], (ushort)0x5A4D);    // "MZ"
        BitConverter.TryWriteBytes(span[0x3C..], 0x40);              // e_lfanew
        BitConverter.TryWriteBytes(span[0x40..], 0x00004550u);       // "PE\0\0"

        // Optional header at 0x40 + 24; the import directory is data directory 1.
        int dataDirectories = 0x40 + 24 + (IntPtr.Size == 8 ? 112 : 96);
        BitConverter.TryWriteBytes(span[(dataDirectories + 8)..], ImportTable);

        // One descriptor, then the all-zero terminator.
        BitConverter.TryWriteBytes(span[(ImportTable + 0)..], NameThunks);
        BitConverter.TryWriteBytes(span[(ImportTable + 12)..], LibraryName);
        BitConverter.TryWriteBytes(span[(ImportTable + 16)..], AddressThunks);

        nint ordinal = IntPtr.Size == 8 ? unchecked((nint)0x8000_0000_0000_0007) : unchecked((nint)0x8000_0007);
        WriteThunks(span[NameThunks..], [ordinal, MallocName, FreeName, 0]);
        WriteThunks(span[AddressThunks..], [0x1111, 0x2222, 0x3333, 0]);

        // IMAGE_IMPORT_BY_NAME: a two byte hint, then the name.
        Encoding.ASCII.GetBytes("malloc\0").CopyTo(span[(MallocName + 2)..]);
        Encoding.ASCII.GetBytes("free\0").CopyTo(span[(FreeName + 2)..]);
        Encoding.ASCII.GetBytes("msvcrt.dll\0").CopyTo(span[LibraryName..]);

        return image;
    }

    private static void WriteThunks(Span<byte> destination, nint[] thunks)
    {
        MemoryMarshal.AsBytes(thunks.AsSpan()).CopyTo(destination);
    }

    [Theory]
    [InlineData("free", 0x3333)]
    [InlineData("malloc", 0x2222)]
    [InlineData("realloc", 0)]
    [InlineData("fre", 0)]
    public void FindImport_ReturnsTheBoundAddressOfANamedImport(string name, int expected)
    {
        fixed (byte* image = SyntheticImage())
        {
            Assert.Equal(expected, (int)LiblouisHeap.FindImport(image, Encoding.ASCII.GetBytes(name)));
        }
    }

    /// <summary>
    /// Without OriginalFirstThunk the names have been overwritten by addresses, so the descriptor
    /// cannot be searched - it must be skipped rather than read as names.
    /// </summary>
    [Fact]
    public void FindImport_SkipsADescriptorWithoutNames()
    {
        byte[] bytes = SyntheticImage();
        BitConverter.TryWriteBytes(bytes.AsSpan(ImportTable), 0);

        fixed (byte* image = bytes)
        {
            Assert.Equal(0, (int)LiblouisHeap.FindImport(image, "free"u8));
        }
    }

    [Fact]
    public void FindImport_RejectsSomethingThatIsNotAPeImage()
    {
        fixed (byte* image = new byte[0x600])
        {
            Assert.Equal(0, (int)LiblouisHeap.FindImport(image, "free"u8));
        }
    }

    /// <summary>
    /// The real liblouis.dll has to import free() from a C runtime, or every lou_findTable result
    /// would be left unfreed.
    /// </summary>
    [Fact]
    public void FindImport_FindsFreeInTheLiblouisWeShip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Make sure the P/Invoke layer has loaded it.
        _ = LibLouis.Instance.Version;

        nint module = NativeLibrary.Load("liblouis", typeof(LibLouis).Assembly, DllImportSearchPath.SafeDirectories);
        nint free = LiblouisHeap.FindImport((byte*)module, "free"u8);

        nint[] runtimeFrees = [.. new[] { "msvcrt.dll", "ucrtbase.dll" }
            .Select(name => NativeLibrary.TryLoad(name, out nint runtime) ? NativeLibrary.GetExport(runtime, "free") : 0)];

        Assert.NotEqual(0, free);
        Assert.Contains(free, runtimeFrees);
    }

    /// <summary>
    /// Each result is freed into liblouis's heap as it comes back. Releasing it into a different
    /// heap corrupts the one liblouis translates with, so interleave the two and repeat enough for
    /// the corruption to surface as a crash.
    /// </summary>
    [Fact]
    public void FindTable_ResultsCanBeFreedBetweenTranslations()
    {
        LibLouis.Instance.IndexTables(TablePaths());

        for (int k = 0; k < 2000; k++)
        {
            string? table = LibLouis.Instance.FindTable("language:da type:literary grade:2");

            Assert.NotNull(table);
            Assert.EndsWith("da-dk-g26.ctb", table, StringComparison.Ordinal);

            Assert.NotEmpty(LibLouis.Instance.Translate(TablePaths(), "Første linje", 64, null, null, TranslationMode.Regular));
        }
    }
}
