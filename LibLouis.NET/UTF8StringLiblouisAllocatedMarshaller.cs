using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LibLouis.NET;

/// <summary>
/// Marshals a UTF-8 string that liblouis allocated and the caller has to free, freeing it with
/// liblouis's own C runtime.
/// </summary>
/// <remarks>
/// For lou_findTable and the other functions documented as caller-frees. The default UTF-8
/// marshalling would free the result too, but with CoTaskMemFree on Windows: a different heap from
/// the one liblouis allocated from, which corrupts it. <see cref="LiblouisHeap"/> frees it into the
/// right one.
///
/// Restricted to <see cref="MarshalMode.ManagedToUnmanagedOut"/> for the same reason as
/// <see cref="UTF8StringNoFreeMarshaller"/>: only memory liblouis allocated belongs in its heap.
/// </remarks>
[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedOut, typeof(UTF8StringLiblouisAllocatedMarshaller))]
internal static unsafe class UTF8StringLiblouisAllocatedMarshaller
{
    /// <summary>
    /// Copies the NUL terminated UTF-8 string at <paramref name="unmanaged"/> into a managed string.
    /// </summary>
    public static string? ConvertToManaged(byte* unmanaged)
    {
        return Marshal.PtrToStringUTF8((nint)unmanaged);
    }

    /// <summary>
    /// Returns the string to the heap liblouis allocated it from.
    /// </summary>
    public static void Free(byte* unmanaged)
    {
        LiblouisHeap.Free(unmanaged);
    }
}
