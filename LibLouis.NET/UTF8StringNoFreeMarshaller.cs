using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace LibLouis.NET;

[CustomMarshaller(typeof(string), MarshalMode.Default, typeof(UTF8StringNoFreeMarshaller))]
public unsafe static class UTF8StringNoFreeMarshaller
{
    public const byte NullTerminator = (byte)0;

    public static byte* ConvertToUnmanaged(string? managedString)
    {
        if (managedString is null)
        {
            return null;
        }
        
        int unmanagedLength = Encoding.UTF8.GetByteCount(managedString) + 1;
        byte* bufferPointer = (byte*)NativeMemory.Alloc((nuint)unmanagedLength);
        Span<byte> byteSpan = new(bufferPointer, unmanagedLength);

        byteSpan = Encoding.UTF8.GetBytes(managedString);
        byteSpan[^1] = NullTerminator;

        return bufferPointer;
    }


    public static string? ConvertToManaged(byte* unmanaged)
    {
        if (unmanaged == null)
        {
            return null;
        }

        Span<byte> stringSpan = new(unmanaged, int.MaxValue);      
        int length = stringSpan.IndexOf(NullTerminator);

        return Encoding.UTF8.GetString(unmanaged, length);
    }


    public static void Free(byte* unmanaged)
    {
        // Do nothing, not caller's responsiblity to free it.
    }
}
