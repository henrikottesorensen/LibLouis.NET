using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LibLouis.NET;

public class LibLouis
{
    /// <summary>
    /// LibLouis loglevels to ILogger logLevels table.
    /// </summary>
    private static readonly Dictionary<LogLevel, Microsoft.Extensions.Logging.LogLevel> LogLevels = new()
    {
        { LogLevel.All, Microsoft.Extensions.Logging.LogLevel.Trace },
        { LogLevel.Debug, Microsoft.Extensions.Logging.LogLevel.Debug },
        { LogLevel.Info, Microsoft.Extensions.Logging.LogLevel.Information },
        { LogLevel.Warning, Microsoft.Extensions.Logging.LogLevel.Warning },
        { LogLevel.Error, Microsoft.Extensions.Logging.LogLevel.Error },
        { LogLevel.Fatal, Microsoft.Extensions.Logging.LogLevel.Critical },
        { LogLevel.Off,  Microsoft.Extensions.Logging.LogLevel.None },
    };

    /// <summary>
    /// LibLouis is *NOT* thread safe, so we'll have to use a lock to avoid concurrrent access to native liblouis calls.
    /// </summary>
    /// <remarks>
    /// Static, and shared with <see cref="Logging"/>: the state it protects belongs to the native
    /// library, not to this instance, so every native call in the assembly has to serialise on the
    /// same object. Monitor is reentrant, so a logger that calls back in while liblouis is logging
    /// does not deadlock.
    /// </remarks>
    internal static readonly object NativeLock = new();

    /// <summary>
    /// LibLouis can currently use either UCS-4 (1:1 mapping of UTF-32), or UCS-2 (WTF-16 without surrogate pairs),
    /// as it's internal widechar representation. LibLouisStringEncoder is set to get the detected representation
    /// of the native liblouis library.
    /// </summary>
    private readonly Encoding LibLouisStringEncoder;

    /// <summary>
    /// How many bytes liblouis uses to represent a single Unicode character. (2 => UCS-2, 4 => UCS-4)
    /// </summary>
    private readonly int CharacterSize;

    /// <summary>
    /// Singleton LibLouis instance accessor.
    /// </summary>
    public static LibLouis Instance { get; private set; }

    private string _lastLogMessage = string.Empty;

    /// <summary>
    /// Roots the delegate behind the function pointer liblouis holds.
    /// </summary>
    /// <remarks>
    /// The interop stub only keeps the delegate alive for the duration of the registration call,
    /// but liblouis keeps calling the pointer for the rest of the process's life. Without a
    /// reference here the delegate is collected and the next native log message kills the process
    /// with "A callback was made on a garbage collected delegate".
    /// </remarks>
    private readonly NativeMethods.LoggingCallback _logCallback;

    static LibLouis()
    {
        Instance = new LibLouis();
    }

    private LibLouis()
    {
        // unlocked: the type initializer runs single threaded, and no other thread can hold a
        // reference to the singleton until it has finished, so there is nothing to race with.
        CharacterSize = NativeMethods.lou_charSize();
        LibLouisStringEncoder = CharacterSize switch
        {
            2 => Encoding.Unicode, // UTF-16LE, LibLouis uses UCS-2, but UTF-16 should be compatible for MOST of the range.
            4 => Encoding.UTF32,   // UTF-32 is a direct mapping of UCS-4.
            _ => throw new NotImplementedException($"Liblouis is a character size of {CharacterSize}!?"),
        };

        _logCallback = LogCallback;

        // Register managed log callback, so we can give reasonable exception messages.
        // unlocked: same reason - still inside the type initializer.
        NativeMethods.lou_registerLogCallback(_logCallback);
    }

    // Deliberately no finalizer. lou_free tears down state that is global to the process, while a
    // finalizer runs per managed instance: in a collectible AssemblyLoadContext it would free the
    // tables of every other context still using liblouis, from the finalizer thread, outside the
    // lock. Nothing here owns a handle that leaks if Shutdown is never called.

    private ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Read by the guards without the lock, written by <see cref="Shutdown"/> under it.
    /// </summary>
    /// <remarks>
    /// Static because what it tracks is the state of the native library, not of this instance.
    /// </remarks>
    private static volatile bool _shutDown;

    /// <summary>
    /// ILogger instance LibLouis will log to.
    /// </summary>
    public ILogger Logger
    {
        get => _logger;
        set => SetLogger(value);
    }

    private void SetLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        // Deliberately usable after disposal: neither call touches anything lou_free released,
        // and being able to attach a logger while shutting down is worth more than the symmetry.
        lock (NativeLock)
        {
            _logger = logger;
            NativeMethods.lou_setLogLevel(LogLevel.All);
            NativeMethods.lou_registerLogCallback(_logCallback);
        }
    }

    /// <summary>
    /// Called by liblouis, on a native stack.
    /// </summary>
    /// <remarks>
    /// Nothing may be thrown out of here. liblouis has no way to handle a managed exception, and
    /// letting one unwind through its frames tears the process down.
    /// </remarks>
    private void LogCallback(LogLevel level, string message)
    {
        try
        {
            _lastLogMessage = message;

            // liblouis is free to introduce log levels we have no mapping for.
            if (!LogLevels.TryGetValue(level, out Microsoft.Extensions.Logging.LogLevel l))
            {
                l = Microsoft.Extensions.Logging.LogLevel.Information;
            }

            if (_logger.IsEnabled(l))
            {
                // Passed as an argument, not as the template: liblouis messages contain table
                // paths and rule text, and a stray brace would otherwise be parsed as a
                // placeholder.
                _logger.Log(l, "{LiblouisMessage}", message);
            }
        }
        catch
        {
            // A logger that throws must not become a native crash.
        }
    }

    /// <summary>
    /// Returns version number of the native liblouis library.
    /// </summary>
    /// <remarks>
    /// Readable after disposal: lou_version returns a compile-time constant and touches nothing
    /// lou_free released, and version information is worth having while diagnosing a shutdown.
    /// </remarks>
    public string Version
    {
        get
        {
            lock (NativeLock)
            {
                return NativeMethods.lou_version();
            }
        }
    }

    /// <summary>
    /// This property is used to tell liblouis and liblouisutdml where tables and files are located. It thus makes them completely relocatable, even on Linux. 
    /// The path is the directory where the subdirectories liblouis/tables and liblouisutdml/lbu_files are rooted or located.
    /// </summary>
    public string? DataPath
    {
        get
        {
            lock (NativeLock)
            {
                ThrowIfShutDown();
                return NativeMethods.lou_getDataPath();
            }
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
            lock (NativeLock)
            {
                ThrowIfShutDown();
                NativeMethods.lou_setDataPath(value);
            }
        }
    }

    /// <summary>
    /// This function can be used to find a table based on metadata. query is a string in the special query syntax. 
    /// It is matched against table metadata inside the tables that were previously indexed with IndexTables().  
    /// </summary>
    /// <param name="query">A query that is passed to the FindTable() function must have the following syntax:
    /// <feature1> <feature2> <feature3> ...
    ///  where ‘<feature>’ is either:
    /// <key>:<value>
    /// or:
    /// <key>
    /// </param>
    /// <returns>Returns the file name of the best match. Returns NULL if the query is invalid or if no match can be found.</returns>
    public string? FindTable(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        lock (NativeLock)
        {
            ThrowIfShutDown();
            return NativeMethods.lou_findTable(query);
        }
    }

    /// <summary>
    /// This function must be called prior to lou_findTable. It parses, analyzes and indexes all specified tables. Tables that contain invalid metadata are ignored. 
    /// </summary>
    /// <param name="tables">tables must be an IEnumerable of file names.</param>
    public void IndexTables(IEnumerable<string> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        // liblouis walks the array until it reads a null pointer, so it needs a terminator on top
        // of the table names. Without it, it reads whatever managed memory follows the array and
        // hands it to _lou_logMessage as a string.
        string?[] nullTerminated = [.. tables, null];

        lock (NativeLock)
        {
            ThrowIfShutDown();
            NativeMethods.lou_indexTables(nullTerminated);
        }
    }

    /// <summary>
    /// This function takes a string in input consisting of dot patterns and converts it to a string in output consisting of characters according to the specifications in tableList.
    /// </summary>
    /// <param name="input">The dot patterns in input can be in either liblouis format or Unicode braille.</param>
    /// <returns></returns>
    public string DotsToCharacters(IEnumerable<string> tableList, string input)
    {
        ArgumentNullException.ThrowIfNull(input, nameof(input));

        int length = CountUCSCharacters(input);

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(length);

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_dotsToChar(tables, inputBuffer, outputBuffer, length, TranslationMode.Regular) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return ConvertUCSOutputBufferToString(outputBuffer, length);
    }

    /// <summary>
    /// This function takes a string in input consisting of dot patterns and converts it to a string in output consisting of characters according to the specifications in tableList.
    /// </summary>
    /// <param name="input">The dot patterns in input can be in either liblouis format or Unicode braille.</param>
    /// <returns></returns>
    public string CharactersToDots(IEnumerable<string> tableList, string input)
    {
        ArgumentNullException.ThrowIfNull(input, nameof(input));

        int length = CountUCSCharacters(input);

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(length);

        string tables = string.Join(',', tableList);

        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_charToDots(tables, inputBuffer, outputBuffer, length, TranslationMode.Regular) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return ConvertUCSOutputBufferToString(outputBuffer, length);
    }

    /// <summary>
    /// This function takes a string of Unicode characters in inbuf and translates it into a string of characters in outbuf. 
    /// Each character produces a particular dot pattern in one braille cell when sent to an embosser or braille display or to a screen type font.
    /// Which character represents which dot pattern is indicated by the character-definition and display opcodes in the translation table. 
    /// </summary>
    /// <param name="tableList">The tableList parameter points to a list of translation tables. See How tables are found, for a description on how the tables are located in the file system. If only one table is given, no comma should be used after it. It is these tables which control just how the translation is made, whether in Grade 2, Grade 1, or something else.</param>
    /// <param name="input">String to translate.</param>
    /// <param name="outputLength">Maximum output length.</param>
    /// <param name="formtype">The typeform parameter is used to indicate italic type, boldface type, computer braille, etc. It is an array of formtype with the same length as the input buffer pointed to by input. Each element indicates the typeform of the corresponding character in the input buffer. </param>
    /// <param name="spacing">The spacing parameter is used to indicate differences in spacing between the input string and the translated output string. It is also of the same length as the input string. If this parameter is NULL, no spacing information is computed. </param>
    /// <param name="outputPosition"></param>
    /// <param name="inputPosition"></param>
    /// <param name="cursorPosition"></param>
    /// <param name="mode">The mode parameter specifies how the translation should be done. They are all powers of 2, so that a combined mode can be specified by adding up different values.</param>
    /// <returns>Translated string</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="LibLouisException"></exception>
    public TranslatedString Translate(
        IEnumerable<string> tableList,
        string input,
        int outputLength,
        TypeForm[]? formtype,
        string? spacing,
        int[] outputPosition,
        int[] inputPosition,
        int cursorPosition,
        TranslationMode mode)
    {
        if (outputLength < 1)
        {
            throw new ArgumentException($"{nameof(outputLength)} must be over 0", nameof(outputLength));
        }

        if (spacing is not null && input.Length != spacing.Length)
        {
            throw new ArgumentException($"{nameof(spacing)} must be the same length as input or null");
        }

        if (inputPosition.Length < outputLength)
        {
            throw new ArgumentException($"{nameof(inputPosition)} must be an array of integers of at least outputLength elements.", nameof(inputPosition));
        }

        if (outputPosition.Length < input.Length)
        {
            throw new ArgumentException($"{nameof(outputPosition)} parameter must point to an array of integers with at least input length elements.", nameof(outputPosition));
        }

        // The number of widechars to translate, excluding the NUL terminator, which is what the
        // header means by inlen and what upstream callers pass. The buffer stays terminated: the
        // translate functions clamp at the first NUL, so an embedded NUL still ends the input.
        int inputLength = CountUCSCharacters(input);
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);
        TypeForm[]? typeFormBuffer = PrepareTypeFormBuffer(formtype, inputLength, outputBufferLength);

        // The cursor arrives as a .NET string index and liblouis wants a widechar index.
        int[] inputOffsets = Utf16OffsetOfWidechar(input);
        int widecharCursor = ToWidecharCursor(input, cursorPosition);

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_translate(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, typeFormBuffer, spacing, outputPosition, inputPosition, ref widecharCursor, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        string output = ConvertUCSOutputBufferToString(outputBuffer, outputLength);

        (int[] mappedOutputPosition, int[] mappedInputPosition, int mappedCursor) =
            MapPositionsToUtf16(input, output, inputOffsets, outputPosition, inputPosition, widecharCursor);

        return new TranslatedString
        {
            Output = output,
            CursorPosition = mappedCursor,
            InputPosition = mappedInputPosition,
            OutputPosition = mappedOutputPosition,
            OutputDots78 = ExtractOutputDots78(typeFormBuffer, outputLength),
        };
    }

    /// <summary>
    /// This function takes a string of Unicode characters in inbuf and translates it into a string of characters in outbuf. 
    /// Each character produces a particular dot pattern in one braille cell when sent to an embosser or braille display or to a screen type font.
    /// Which character represents which dot pattern is indicated by the character-definition and display opcodes in the translation table. 
    /// </summary>
    /// <param name="tableList">The tableList parameter points to a list of translation tables. See How tables are found, for a description on how the tables are located in the file system. If only one table is given, no comma should be used after it. It is these tables which control just how the translation is made, whether in Grade 2, Grade 1, or something else.</param>
    /// <param name="input">String to translate.</param>
    /// <param name="outputLength">Maximum output length.</param>
    /// <param name="formtype">The typeform parameter is used to indicate italic type, boldface type, computer braille, etc. It is an array of formtype with the same length as the input buffer pointed to by input. Each element indicates the typeform of the corresponding character in the input buffer. </param>
    /// <param name="spacing">The spacing parameter is used to indicate differences in spacing between the input string and the translated output string. It is also of the same length as the input string. If this parameter is NULL, no spacing information is computed. </param>
    /// <param name="mode">The mode parameter specifies how the translation should be done. They are all powers of 2, so that a combined mode can be specified by adding up different values.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="LibLouisException"></exception>
    public string Translate(IEnumerable<string> tableList, string input, int outputLength, TypeForm[]? formtype, string? spacing, TranslationMode mode)
    {
        if (outputLength < 1)
        {
            throw new ArgumentException("Output length must be over 0", nameof(outputLength));
        }

        if (spacing is not null && input.Length != spacing.Length)
        {
            throw new ArgumentException("Spacing must be the same length as input or null");
        }

        // The number of widechars to translate, excluding the NUL terminator, which is what the
        // header means by inlen and what upstream callers pass. The buffer stays terminated: the
        // translate functions clamp at the first NUL, so an embedded NUL still ends the input.
        int inputLength = CountUCSCharacters(input);
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);
        TypeForm[]? typeFormBuffer = PrepareTypeFormBuffer(formtype, inputLength, outputBufferLength);

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_translateString(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, typeFormBuffer, spacing, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return ConvertUCSOutputBufferToString(outputBuffer, outputLength);

    }

    /// <summary>
    /// This function takes a string of Unicode characters in inbuf and translates it into a string of characters in outbuf. 
    /// Each character produces a particular dot pattern in one braille cell when sent to an embosser or braille display or to a screen type font.
    /// Which character represents which dot pattern is indicated by the character-definition and display opcodes in the translation table. 
    /// </summary>
    /// <param name="tableList">The tableList parameter points to a list of translation tables. See How tables are found, for a description on how the tables are located in the file system. If only one table is given, no comma should be used after it. It is these tables which control just how the translation is made, whether in Grade 2, Grade 1, or something else.</param>
    /// <param name="input">String to translate.</param>
    /// <param name="outputLength">Maximum output length.</param>
    /// <param name="formtype">The typeform parameter is used to indicate italic type, boldface type, computer braille, etc. It is an array of formtype with the same length as the input buffer pointed to by input. Each element indicates the typeform of the corresponding character in the input buffer. </param>
    /// <param name="spacing">The spacing parameter is used to indicate differences in spacing between the input string and the translated output string. It is also of the same length as the input string. If this parameter is NULL, no spacing information is computed. </param>
    /// <param name="outputPosition"></param>
    /// <param name="inputPosition"></param>
    /// <param name="cursorPosition"></param>
    /// <param name="mode">The mode parameter specifies how the translation should be done. They are all powers of 2, so that a combined mode can be specified by adding up different values.</param>
    /// <returns>Translated string</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="LibLouisException"></exception>
    public TranslatedString BackTranslate(
        IEnumerable<string> tableList,
        string input,
        int outputLength,
        TypeForm[]? formtype,
        string? spacing,
        int[] outputPosition,
        int[] inputPosition,
        int cursorPosition,
        TranslationMode mode)
    {
        if (outputLength < 1)
        {
            throw new ArgumentException($"{nameof(outputLength)} must be over 0", nameof(outputLength));
        }

        if (spacing is not null && input.Length != spacing.Length)
        {
            throw new ArgumentException($"{nameof(spacing)} must be the same length as input or null");
        }

        if (inputPosition.Length < outputLength)
        {
            throw new ArgumentException($"{nameof(inputPosition)} must be an array of integers of at least outputLength elements.", nameof(inputPosition));
        }

        if (outputPosition.Length < input.Length)
        {
            throw new ArgumentException($"{nameof(outputPosition)} parameter must point to an array of integers with at least input length elements.", nameof(outputPosition));
        }

        // The number of widechars to translate, excluding the NUL terminator, which is what the
        // header means by inlen and what upstream callers pass. The buffer stays terminated: the
        // translate functions clamp at the first NUL, so an embedded NUL still ends the input.
        int inputLength = CountUCSCharacters(input);
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);
        TypeForm[]? typeFormBuffer = PrepareTypeFormBuffer(formtype, inputLength, outputBufferLength);

        // The cursor arrives as a .NET string index and liblouis wants a widechar index.
        int[] inputOffsets = Utf16OffsetOfWidechar(input);
        int widecharCursor = ToWidecharCursor(input, cursorPosition);

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_backTranslate(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, typeFormBuffer, spacing, outputPosition, inputPosition, ref widecharCursor, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        string output = ConvertUCSOutputBufferToString(outputBuffer, outputLength);

        (int[] mappedOutputPosition, int[] mappedInputPosition, int mappedCursor) =
            MapPositionsToUtf16(input, output, inputOffsets, outputPosition, inputPosition, widecharCursor);

        return new TranslatedString
        {
            Output = output,
            CursorPosition = mappedCursor,
            InputPosition = mappedInputPosition,
            OutputPosition = mappedOutputPosition,
        };
    }

    /// <summary>
    /// This is exactly the opposite of Translate. input is a string of Unicode characters representing braille. Return value will contain a string of Unicode characters.
    /// </summary>
    /// <param name="tableList">The tableList parameter points to a list of translation tables. See How tables are found, for a description on how the tables are located in the file system. If only one table is given, no comma should be used after it. It is these tables which control just how the translation is made, whether in Grade 2, Grade 1, or something else.</param>
    /// <param name="input">String to translate.</param>
    /// <param name="outputLength">Maximum output length.</param>
    /// <param name="formtype">The typeform parameter is used to indicate italic type, boldface type, computer braille, etc. It is an array of formtype with the same length as the input buffer pointed to by input. Each element indicates the typeform of the corresponding character in the input buffer. </param>
    /// <param name="spacing">The spacing parameter is used to indicate differences in spacing between the input string and the translated output string. It is also of the same length as the input string. If this parameter is NULL, no spacing information is computed. </param>
    /// <param name="mode">The mode parameter specifies how the translation should be done. They are all powers of 2, so that a combined mode can be specified by adding up different values.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="LibLouisException"></exception>
    public string BackTranslate(IEnumerable<string> tableList, string input, int outputLength, TypeForm[]? formtype, string? spacing, TranslationMode mode)
    {
        if (outputLength < 1)
        {
            throw new ArgumentException("Output length must be over 0", nameof(outputLength));
        }

        if (spacing is not null && input.Length != spacing.Length)
        {
            throw new ArgumentException("Spacing must be the same length as input or null");
        }

        // The number of widechars to translate, excluding the NUL terminator, which is what the
        // header means by inlen and what upstream callers pass. The buffer stays terminated: the
        // translate functions clamp at the first NUL, so an embedded NUL still ends the input.
        int inputLength = CountUCSCharacters(input);
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);
        TypeForm[]? typeFormBuffer = PrepareTypeFormBuffer(formtype, inputLength, outputBufferLength);

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_backTranslateString(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, typeFormBuffer, spacing, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return ConvertUCSOutputBufferToString(outputBuffer, outputLength);
    }

    /// <summary>
    /// This function looks at the characters in inbuf and if it finds a sequence of letters attempts to hyphenate it as a word. 
    /// Note that Hyphenate operates on single words only, and spaces or punctuation marks between letters are not allowed.
    /// Leading and trailing punctuation marks are ignored. The table named by the tableList parameter must contain a hyphenation table. 
    /// If it does not, the function does nothing.
    /// </summary>
    /// <param name="tableList"></param>
    /// <param name="input">The word to hyphenate. Must be shorter than 100 characters.</param>
    /// <param name="mode"></param>
    /// <returns>
    /// One character per character of <paramref name="input"/>: '1' where the word may be broken,
    /// '0' where it may not, '2' after an existing hyphen. On a UCS-4 build a non-BMP character
    /// counts once, so the result can be shorter than <paramref name="input"/>.
    /// </returns>
    /// <exception cref="LibLouisException"></exception>
    public string Hyphenate(IEnumerable<string> tableList, string input, TranslationMode mode)
    {
        ArgumentNullException.ThrowIfNull(tableList);
        ArgumentException.ThrowIfNullOrEmpty(input);

        int length = CountUCSCharacters(input);

        // liblouis rejects anything from HYPHSTRING characters up, and would otherwise report it
        // as an ordinary hyphenation failure.
        if (length >= MaxHyphenationLength)
        {
            throw new ArgumentException(
                $"{nameof(input)} must be shorter than {MaxHyphenationLength} characters.", nameof(input));
        }

        string tables = string.Join(',', tableList);

        // liblouis writes one flag per character plus a NUL terminator into a caller-allocated
        // char buffer. inlen must not count the terminator: lou_hyphenate memcpy's exactly inlen
        // widechars rather than stopping at a NUL the way the translate functions do, so an
        // inlen in the wrong unit reads straight past the input buffer.
        byte[] hyphens = new byte[length + 1];

        byte[] inputBuffer = PrepareUCSInputBuffer(input);

        bool success;

        lock (NativeLock)
        {
            ThrowIfShutDown();
            success = NativeMethods.lou_hyphenate(tables, inputBuffer, length, hyphens, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"Hyphenation failed {_lastLogMessage}");
        }

        // The flags are ASCII digits; the trailing terminator is not part of the result.
        return Encoding.ASCII.GetString(hyphens, 0, length);
    }

    /// <summary>
    /// liblouis hyphenates into a fixed 100 character buffer (HYPHSTRING) and refuses any input
    /// that would not fit.
    /// </summary>
    private const int MaxHyphenationLength = 100;

    /// <summary>
    /// Copy the caller's typeform values into a buffer that is safe to hand to liblouis.
    /// </summary>
    /// <remarks>
    /// The typeform parameter is in/out: liblouis reads one entry per input character, but on a
    /// successful translation it writes one entry per *output* cell. A translation that grows the
    /// text - which the marker tables do routinely - would therefore write past the end of an
    /// array sized to the input, corrupting the managed heap. We give liblouis a buffer big enough
    /// for both directions and treat the caller's array as input only.
    /// </remarks>
    private static TypeForm[]? PrepareTypeFormBuffer(TypeForm[]? formtype, int inputLength, int outputLength)
    {
        if (formtype is null)
        {
            return null;
        }

        TypeForm[] buffer = new TypeForm[Math.Max(inputLength, outputLength) + 1];
        formtype.AsSpan(0, Math.Min(formtype.Length, buffer.Length)).CopyTo(buffer);

        return buffer;
    }

    /// <summary>
    /// Reads the per-cell dot 7/8 information liblouis wrote into the scratch typeform buffer.
    /// </summary>
    /// <remarks>
    /// The write-back half of <see cref="PrepareTypeFormBuffer"/>: on a successful forward
    /// translation liblouis stores the ASCII character '8' in the slot of every output cell that
    /// contains dot 7 or dot 8, and '0' otherwise (lou_translateString.c:1330). Those are
    /// characters smuggled through a formtype array, not TypeForm flag values, which is why this
    /// converts to booleans instead of exposing the buffer.
    /// </remarks>
    private static bool[]? ExtractOutputDots78(TypeForm[]? typeFormBuffer, int outputLength)
    {
        if (typeFormBuffer is null)
        {
            return null;
        }

        bool[] dots = new bool[outputLength];

        for (int k = 0; k < outputLength; k++)
        {
            dots[k] = typeFormBuffer[k] == (TypeForm)'8';
        }

        return dots;
    }

    /// <summary>
    /// Converts a cursor given as a .NET string index into the widechar index liblouis expects.
    /// </summary>
    /// <remarks>
    /// Negative means "no cursor" to liblouis and is passed through untouched.
    /// </remarks>
    private int ToWidecharCursor(string input, int cursorPosition)
    {
        if (cursorPosition < 0 || input.Length == 0)
        {
            return cursorPosition;
        }

        int[] widechars = WidecharOfUtf16Offset(input);

        return widechars[Math.Clamp(cursorPosition, 0, input.Length - 1)];
    }

    /// <summary>
    /// Rewrites liblouis's widechar-indexed position arrays as UTF-16 indices into the managed
    /// strings, so every value can be used directly as a string index.
    /// </summary>
    /// <remarks>
    /// liblouis counts in widechars: on a UCS-4 build one widechar is a whole Unicode character,
    /// while a .NET string counts UTF-16 code units. The two agree for BMP text and diverge from
    /// the first non-BMP character on, which silently misaligns any caller that treats these
    /// values as string indices - and the arrays exist for nothing else.
    ///
    /// The results are sized to the strings they index rather than to the caller's scratch
    /// buffers, so <c>OutputPosition</c> has one entry per char of the input and
    /// <c>InputPosition</c> one per char of the output. No slicing is required to use them.
    ///
    /// Both halves of a surrogate pair report the same position, since they are one character.
    /// </remarks>
    private (int[] OutputPosition, int[] InputPosition, int CursorPosition) MapPositionsToUtf16(
        string input,
        string output,
        int[] inputOffsets,
        int[] outputWidecharPositions,
        int[] inputWidecharPositions,
        int widecharCursor)
    {
        int[] outputOffsets = Utf16OffsetOfWidechar(output);
        int[] inputWidechars = WidecharOfUtf16Offset(input);
        int[] outputWidechars = WidecharOfUtf16Offset(output);

        int lastInputWidechar = Math.Max(inputOffsets.Length - 2, 0);
        int lastOutputWidechar = Math.Max(outputOffsets.Length - 2, 0);

        int[] outputPosition = new int[input.Length];

        for (int i = 0; i < input.Length; i++)
        {
            int widechar = inputWidechars[i];

            int cell = widechar < outputWidecharPositions.Length ? outputWidecharPositions[widechar] : 0;

            outputPosition[i] = outputOffsets[Math.Clamp(cell, 0, lastOutputWidechar)];
        }

        int[] inputPosition = new int[output.Length];

        for (int t = 0; t < output.Length; t++)
        {
            int widechar = outputWidechars[t];

            int character = widechar < inputWidecharPositions.Length ? inputWidecharPositions[widechar] : 0;

            inputPosition[t] = inputOffsets[Math.Clamp(character, 0, lastInputWidechar)];
        }

        // A negative cursor means "no cursor" to liblouis; leave it alone.
        int cursorPosition = widecharCursor < 0 || output.Length == 0
            ? widecharCursor
            : outputOffsets[Math.Clamp(widecharCursor, 0, lastOutputWidechar)];

        return (outputPosition, inputPosition, cursorPosition);
    }

    /// <summary>
    /// The UTF-16 offset at which each widechar of <paramref name="value"/> starts, with a
    /// sentinel holding the string's length at the end.
    /// </summary>
    private int[] Utf16OffsetOfWidechar(string value)
    {
        int[] offsets = new int[CountUCSCharacters(value) + 1];

        int widechar = 0;

        for (int i = 0; i < value.Length; widechar++)
        {
            offsets[widechar] = i;
            i += IsSurrogatePairAt(value, i) ? 2 : 1;
        }

        offsets[widechar] = value.Length;

        return offsets;
    }

    /// <summary>
    /// The widechar that each UTF-16 offset of <paramref name="value"/> belongs to. Both halves of
    /// a surrogate pair map to the same widechar, because they are one character to liblouis.
    /// </summary>
    private int[] WidecharOfUtf16Offset(string value)
    {
        int[] widechars = new int[value.Length];

        int widechar = 0;

        for (int i = 0; i < value.Length; widechar++)
        {
            int width = IsSurrogatePairAt(value, i) ? 2 : 1;

            for (int k = 0; k < width; k++)
            {
                widechars[i + k] = widechar;
            }

            i += width;
        }

        return widechars;
    }

    /// <summary>
    /// Whether a surrogate pair - one widechar, two chars - starts at <paramref name="index"/>.
    /// Never true on a UCS-2 build, where a widechar is a UTF-16 code unit.
    /// </summary>
    private bool IsSurrogatePairAt(string value, int index)
    {
        return CharacterSize == 4
            && char.IsHighSurrogate(value[index])
            && index + 1 < value.Length
            && char.IsLowSurrogate(value[index + 1]);
    }

    /// <summary>
    /// The number of liblouis widechars <paramref name="input"/> occupies.
    /// </summary>
    /// <remarks>
    /// Not the same as string.Length on a UCS-4 build: a non-BMP character is one widechar but
    /// two chars. Lengths handed to liblouis have to be counted in widechars, or they describe a
    /// longer buffer than the one that was allocated.
    /// </remarks>
    private int CountUCSCharacters(string input)
    {
        return LibLouisStringEncoder.GetByteCount(input) / CharacterSize;
    }

    /// <summary>
    /// Return UCS-2/4 null terminated encoding of input.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    private byte[] PrepareUCSInputBuffer(string input)
    {
        return LibLouisStringEncoder.GetBytes(input + "\0");
    }

    /// <summary>
    /// Return zero filled byte array with room for outputLength UCS-2/4 changers and a null termination.
    /// </summary>
    /// <param name="outputLength"></param>
    /// <returns></returns>
    private byte[] PrepareUCSOutputBuffer(int outputLength)
    {
        byte[] outputBuffer = new byte[(outputLength + 1) * CharacterSize];
        Array.Fill<byte>(outputBuffer, 0);

        return outputBuffer;
    }

    /// <summary>
    /// Convert UCS-2/4 output string to managed string.
    /// </summary>
    /// <param name="outputBuffer"></param>
    /// <param name="outputLength"></param>
    /// <returns></returns>
    private string ConvertUCSOutputBufferToString(byte[] outputBuffer, int outputLength)
    {
        return LibLouisStringEncoder.GetString(outputBuffer, 0, Math.Min(outputLength * CharacterSize, outputBuffer.Length));
    }

    /// <summary>
    /// Throws if liblouis has already been torn down.
    /// </summary>
    /// <remarks>
    /// Called from inside the lock, immediately before the native call. Checking on the way in
    /// instead would leave a window for Shutdown to free the tables between check and call.
    ///
    /// Not ObjectDisposedException: this type is not disposable, and "cannot access a disposed
    /// object" would send the reader looking for a Dispose call that does not exist.
    /// </remarks>
    private static void ThrowIfShutDown()
    {
        if (_shutDown)
        {
            throw new InvalidOperationException(
                "liblouis has been shut down. LibLouis.Shutdown() frees state that is global to "
                + "the process and cannot be undone.");
        }
    }

    /// <summary>
    /// Frees everything liblouis has allocated. Final: there is no way back.
    /// </summary>
    /// <remarks>
    /// Deliberately a static method rather than IDisposable. lou_free walks and frees the
    /// translation and display table chains, which are global to the process, so this is teardown
    /// for the whole application rather than the release of a resource one caller owns. Exposing
    /// it as IDisposable invited <c>using (LibLouis.Instance)</c>, which reads as ordinary
    /// cleanup and would leave every other consumer in the process unable to translate.
    ///
    /// Only worth calling when you need the tables released before the process exits - checking
    /// for leaks, say. Normal applications should not call it at all: liblouis caches compiled
    /// tables per table list rather than per call, so nothing accumulates, and process exit
    /// reclaims it anyway.
    ///
    /// Takes the same lock as every other native call. Freeing those chains while another thread
    /// is translating is a use-after-free, which shows up as anything from a nonsense
    /// "no mapping for dot pattern" error to a crash.
    ///
    /// Calling it more than once does nothing.
    /// </remarks>
    public static void Shutdown()
    {
        lock (NativeLock)
        {
            if (_shutDown)
            {
                return;
            }

            NativeMethods.lou_free();

            _shutDown = true;
        }
    }
}
