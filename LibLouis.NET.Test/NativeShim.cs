using System.Runtime.InteropServices;

namespace LibLouis.NET.Test;

/// <summary>
/// Raw P/Invoke used to characterise native behaviour without going through the wrapper.
/// </summary>
internal static class NativeShim
{
    /// <summary>
    /// Bytes per liblouis widechar: 2 for a UCS-2 build, 4 for UCS-4.
    /// </summary>
    [DllImport("liblouis", EntryPoint = "lou_charSize")]
    internal static extern int lou_charSize();

    [DllImport("liblouis", EntryPoint = "lou_translateString")]
    internal static extern int lou_translateString(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        ushort[]? typeform,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? spacing,
        int mode);
}
