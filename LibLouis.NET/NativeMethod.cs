using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LibLouis.NET;

/// <summary>
/// Native methods for LibLouis.
/// </summary>
public static partial class NativeMethods
{
    /// <summary>
    /// Getting LibLouis version.
    /// </summary>
    /// <returns>LibLouis version.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_version", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(UTF8StringNoFreeMarshaller))]
    internal static partial string lou_version();

    /// <summary>
    /// Translates a string and returns arrays mapping original string to translated string.
    /// </summary>
    /// <param name="tableList">Tables to use for translation.</param>
    /// <param name="inbuf">Input text.</param>
    /// <param name="inlen">Length of input text.</param>
    /// <param name="outbuf">Buffer for output.</param>
    /// <param name="outlen">Length of buffer for output (make sure to allow for additional characters).</param>
    /// <param name="formtype">Formtype is not used.</param>
    /// <param name="spacing">Spacing is not used.</param>
    /// <param name="outputPos">Array of original-to-braille positions.</param>
    /// <param name="inputPos">Array of braille-to-original positions.</param>
    /// <param name="cursorPos">Cursor position is not used.</param>
    /// <param name="mode">Mode is not used.</param>
    /// <returns>0 if error, 1 if success.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_translate", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_translate(
        string tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        TypeForm[]? formtype,
        string? spacing,
        int[] outputPos,
        int[] inputPos,
        ref int cursorPos,
        TranslationMode mode);

    /// <summary>
    /// Translates a string and returns arrays mapping original string to translated string.
    /// </summary>
    /// <param name="tableList">Tables to use for translation.</param>
    /// <param name="inbuf">Input text.</param>
    /// <param name="inlen">Length of input text.</param>
    /// <param name="outbuf">Buffer for output.</param>
    /// <param name="outlen">Length of buffer for output (make sure to allow for additional characters).</param>
    /// <param name="formtype">Formtype is not used.</param>
    /// <param name="spacing">Spacing is not used.</param>
    /// <param name="outputPos">Array of original-to-braille positions.</param>
    /// <param name="inputPos">Array of braille-to-original positions.</param>
    /// <param name="cursorPos">Cursor position is not used.</param>
    /// <param name="mode">Mode is not used.</param>
    /// <returns>0 if error, 1 if success.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_backTranslate", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_backTranslate(
        string tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        TypeForm[]? formtype,
        string? spacing,
        int[] outputPos,
        int[] inputPos,
        ref int cursorPos,
        TranslationMode mode);

    /// <summary>
    /// Translates a string and returns arrays mapping original string to translated string.
    /// </summary>
    /// <param name="tableList">Tables to use for translation.</param>
    /// <param name="inbuf">Input text.</param>
    /// <param name="inlen">Length of input text.</param>
    /// <param name="outbuf">Buffer for output.</param>
    /// <param name="outlen">Length of buffer for output (make sure to allow for additional characters).</param>
    /// <param name="formtype">Formtype is not used.</param>
    /// <param name="spacing">The spacing parameter is used to indicate differences in spacing between the input string and the translated output string. It is also of the same length as the string pointed to by *inbuf. If this parameter is NULL, no spacing information is computed..</param>
    /// <param name="mode">Specifies how the translation should be done. They are all powers of 2, so that a combined mode can be specified by adding up different values. </param>
    /// <returns>0 if error, 1 if success.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_translateString", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_translateString(
        string tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        TypeForm[]? formtype,
        string? spacing,
        TranslationMode mode);

    /// <summary>
    /// This is exactly the opposite of lou_translateString. inbuf is a string of Unicode characters representing braille. outbuf will contain a string of Unicode characters
    /// </summary>
    /// <param name="tableList">Tables to use for translation.</param>
    /// <param name="inbuf">Input text.</param>
    /// <param name="inlen">Length of input text.</param>
    /// <param name="outbuf">Buffer for output.</param>
    /// <param name="outlen">Length of buffer for output (make sure to allow for additional characters).</param>
    /// <param name="formtype">Formtype is not used.</param>
    /// <param name="spacing">Spacing is not used.</param>
    /// <param name="mode">Mode is not used.</param>
    /// <returns>0 if error, 1 if success.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_backTranslateString", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_backTranslateString(
        string tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        TypeForm[]? formtype,
        string? spacing,
        TranslationMode mode);

    /// <summary>
    /// This function looks at the characters in inbuf and if it finds a sequence of letters attempts to hyphenate it as a word.
    /// Note that lou_hyphenate operates on single words only, and spaces or punctuation marks between letters are not allowed.
    /// Leading and trailing punctuation marks are ignored.
    /// </summary>
    /// <param name="tableList">Contains a hyphenation table.</param>
    /// <param name="inbuf">length of the character string in inbuf.</param>
    /// <param name="inlen">inlen is the length of the character string in inbuf</param>
    /// <param name="hyphens">array of characters and must be of size inlen + 1 (to account for the NULL terminator).</param>
    /// <param name="mode"></param>
    /// <returns>0 if error, 1 if success.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_hyphenate", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_hyphenate(string tableList, byte[] inbuf, int inlen, ref string hyphens, TranslationMode mode);

    /// <summary>
    /// This function enables you to compile a table entry on the fly at run-time. 
    /// The new entry is added to tableList and remains in force until lou_free is called. 
    /// If tableList has not previously been loaded it is loaded and compiled. inString contains the table entry to be added.
    /// It may be anything valid. Error messages will be produced if it is invalid. 
    ///. 
    /// </summary>
    /// <param name="tableList"></param>
    /// <param name="inbuf">Contains the table entry to be added. It may be anything valid.</param>
    /// <returns>0 if error, 1 if success.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_compileString", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_compileString(string tableList, byte[] inbuf);

    /// <summary>
    /// Frees up memory after use. Only call when disposing object.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_free")]
    internal static partial void lou_free();

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_charSize")]
    internal static partial int lou_charSize();

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_setLogLevel")]
    internal static partial void lou_setLogLevel(LogLevel level);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void LoggingCallback(LogLevel level, [MarshalAs(UnmanagedType.LPStr)] string message);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_registerLogCallback")]
    internal static partial void lou_registerLogCallback(LoggingCallback callback);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_getDataPath", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial string lou_getDataPath();

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_setDataPath", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial string lou_setDataPath(string path);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_checkTable", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_checkTable(string tableList);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_indexTables", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void lou_indexTables(string[] tables);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_findTable", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial string lou_findTable(string query);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_compileString", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_compileString(string tableList, string inString);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_dotsToChar", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_dotsToChar(string tableList, byte[] inBuf, byte[] outBuf, int length, TranslationMode mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("liblouis", EntryPoint = "lou_charToDots", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int lou_charToDots(string tableList, byte[] inBuf, byte[] outBuf, int length, TranslationMode mode);
}
