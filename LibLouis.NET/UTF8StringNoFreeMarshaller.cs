using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LibLouis.NET;

/// <summary>
/// Marshals a UTF-8 string that liblouis owns, without freeing it.
/// </summary>
/// <remarks>
/// Several liblouis functions return a <c>char *</c> the caller must not release: lou_version,
/// lou_getDataPath and lou_setDataPath all hand back a pointer into static storage inside the
/// library. The default UTF-8 marshalling frees whatever the callee returned, which for those
/// pointers aborts the process ("pointer being freed was not allocated").
///
/// It is deliberately restricted to <see cref="MarshalMode.ManagedToUnmanagedOut"/> - return
/// values and out parameters. Not freeing is only correct for memory we did not allocate;
/// applying it to an input parameter would leak the buffer allocated for every call, so
/// parameters keep using the built-in <see cref="Utf8StringMarshaller"/>.
///
/// liblouis also has functions whose result the caller *is* expected to free (lou_findTable,
/// lou_findTables, lou_getTableInfo, lou_listTables). Those use this marshaller too: the Windows
/// binaries are built with mingw-w64 and allocate from msvcrt.dll while .NET frees through
/// ucrtbase.dll, so releasing that memory from managed code would corrupt the heap. Leaking a
/// bounded number of small strings is the safer trade.
/// </remarks>
[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedOut, typeof(UTF8StringNoFreeMarshaller))]
public static unsafe class UTF8StringNoFreeMarshaller
{
    /// <summary>
    /// Copies the NUL terminated UTF-8 string at <paramref name="unmanaged"/> into a managed string.
    /// </summary>
    public static string? ConvertToManaged(byte* unmanaged)
    {
        return Marshal.PtrToStringUTF8((nint)unmanaged);
    }

    /// <summary>
    /// Deliberately does nothing: the string belongs to liblouis.
    /// </summary>
    public static void Free(byte* unmanaged)
    {
    }
}
