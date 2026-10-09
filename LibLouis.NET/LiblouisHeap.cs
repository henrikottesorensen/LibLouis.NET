using System;
using System.Runtime.InteropServices;

namespace LibLouis.NET;

/// <summary>
/// Releases memory that liblouis allocated and hands to the caller to free, through the C runtime
/// liblouis allocated it from.
/// </summary>
/// <remarks>
/// liblouis has no function of its own for this: lou_findTable and friends return memory from
/// their C runtime's malloc and expect the caller to call that runtime's free.
///
/// Off Windows there is one C runtime per process, and <see cref="NativeMemory.Free"/> is its
/// free().
///
/// On Windows there can be several, each with its own heap, and which one liblouis uses depends on
/// how it was built: our x64 and x86 binaries come from mingw-w64 and use msvcrt.dll, win-arm64
/// comes from llvm-mingw and uses the UCRT. .NET's own string marshalling frees with
/// CoTaskMemFree, which is neither. Releasing memory into the wrong heap corrupts it, so rather
/// than guess the runtime from the RID, this reads liblouis's import table and calls the exact
/// free() its own calls resolve to.
/// </remarks>
internal static unsafe class LiblouisHeap
{
    private const ushort DosSignature = 0x5A4D;   // "MZ"
    private const uint PeSignature = 0x00004550;  // "PE\0\0"
    private const int ImportDirectoryIndex = 1;

    private static readonly Lazy<nint> WindowsFree = new(FindImportedFree);

    /// <summary>
    /// Frees <paramref name="pointer"/>, which liblouis allocated. A null pointer is ignored.
    /// </summary>
    /// <remarks>
    /// On a Windows liblouis that does not import free() - one linked against a static C runtime -
    /// there is no way to reach its heap, and the memory is left alone rather than released into
    /// the wrong one. None of our builds are linked that way.
    /// </remarks>
    public static void Free(void* pointer)
    {
        if (pointer is null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            NativeMemory.Free(pointer);
            return;
        }

        nint free = WindowsFree.Value;

        if (free != 0)
        {
            ((delegate* unmanaged[Cdecl]<void*, void>)free)(pointer);
        }
    }

    /// <summary>
    /// The address liblouis's import of free() is bound to, or 0 if it imports none.
    /// </summary>
    private static nint FindImportedFree()
    {
        // The same search the P/Invoke stubs use, so this is the module they already loaded.
        nint module = NativeLibrary.Load("liblouis", typeof(LiblouisHeap).Assembly, DllImportSearchPath.SafeDirectories);

        return FindImport((byte*)module, "free"u8);
    }

    /// <summary>
    /// Looks <paramref name="name"/> up in the import table of the PE image mapped at
    /// <paramref name="image"/>, and returns what the loader bound it to.
    /// </summary>
    /// <remarks>
    /// Walks the image as loaded, so every offset is an RVA from <paramref name="image"/>. The
    /// image matches the process's bitness - it could not have been loaded otherwise - so thunks
    /// are pointer sized and an import by ordinal is one with the top bit set.
    /// </remarks>
    internal static nint FindImport(byte* image, ReadOnlySpan<byte> name)
    {
        if (*(ushort*)image != DosSignature)
        {
            return 0;
        }

        byte* ntHeaders = image + *(int*)(image + 0x3C);

        if (*(uint*)ntHeaders != PeSignature)
        {
            return 0;
        }

        // PE signature (4) + file header (20), then the optional header, whose data directories
        // sit after 96 bytes in PE32 and 112 in PE32+.
        byte* optionalHeader = ntHeaders + 24;
        int dataDirectories = sizeof(nint) == 8 ? 112 : 96;
        uint importTable = *(uint*)(optionalHeader + dataDirectories + (ImportDirectoryIndex * 8));

        if (importTable == 0)
        {
            return 0;
        }

        // IMAGE_IMPORT_DESCRIPTOR: OriginalFirstThunk, TimeDateStamp, ForwarderChain, Name,
        // FirstThunk - five uints, ended by an all-zero entry.
        for (uint* descriptor = (uint*)(image + importTable); descriptor[3] != 0; descriptor += 5)
        {
            // OriginalFirstThunk keeps the names; FirstThunk is the table the loader overwrote
            // with addresses. Without the former the names are gone.
            uint names = descriptor[0];
            uint addresses = descriptor[4];

            if (names == 0)
            {
                continue;
            }

            nint* nameThunks = (nint*)(image + names);
            nint* addressThunks = (nint*)(image + addresses);

            for (int k = 0; nameThunks[k] != 0; k++)
            {
                if (nameThunks[k] < 0)
                {
                    continue; // imported by ordinal
                }

                // IMAGE_IMPORT_BY_NAME: a two byte hint, then the NUL terminated name.
                byte* importName = image + (uint)nameThunks[k] + 2;

                if (MemoryMarshal.CreateReadOnlySpanFromNullTerminated(importName).SequenceEqual(name))
                {
                    return addressThunks[k];
                }
            }
        }

        return 0;
    }
}
