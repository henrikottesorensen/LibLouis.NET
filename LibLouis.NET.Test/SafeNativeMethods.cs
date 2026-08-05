using System.Runtime.InteropServices;
using System.Text;

namespace LibLouis.NET.Test;

/// <summary>
/// Raw P/Invoke used to characterise native behaviour without going through the wrapper.
/// </summary>
/// <remarks>
/// Strings are passed as pre-encoded NUL terminated UTF-8 rather than as managed strings, so
/// there is no marshalling behaviour of our own between the test and liblouis.
/// </remarks>
internal static class SafeNativeMethods
{
    /// <summary>
    /// Bytes per liblouis widechar: 2 for a UCS-2 build, 4 for UCS-4.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("liblouis", EntryPoint = "lou_charSize")]
    internal static extern int lou_charSize();

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("liblouis", EntryPoint = "lou_translateString")]
    internal static extern int lou_translateString(
        byte[] tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        ushort[]? typeform,
        byte[]? spacing,
        int mode);

    /// <summary>
    /// Encodes a string the way liblouis expects a <c>const char *</c>.
    /// </summary>
    internal static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value + "\0");
    }
}
