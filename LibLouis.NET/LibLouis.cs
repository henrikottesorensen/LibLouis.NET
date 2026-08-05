using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LibLouis.NET;

public class LibLouis : IDisposable
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
    // lock. Nothing here owns a handle that needs releasing if the caller forgets to dispose.

    private ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Read by the guards without the lock, written by <see cref="Dispose(bool)"/> under it.
    /// </summary>
    private volatile bool disposedValue;

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
                ThrowIfDisposed();
                return NativeMethods.lou_getDataPath();
            }
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
            lock (NativeLock)
            {
                ThrowIfDisposed();
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
            ThrowIfDisposed();
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
            ThrowIfDisposed();
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
            ThrowIfDisposed();
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
            ThrowIfDisposed();
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

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfDisposed();
            success = NativeMethods.lou_translate(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, typeFormBuffer, spacing, outputPosition, inputPosition, ref cursorPosition, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return new TranslatedString
        {
            Output = ConvertUCSOutputBufferToString(outputBuffer, outputLength),
            CursorPosition = cursorPosition,
            InputPosition = inputPosition,
            OutputPosition = outputPosition,
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
            ThrowIfDisposed();
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

        string tables = string.Join(',', tableList);
        bool success;

        lock (NativeLock)
        {
            ThrowIfDisposed();
            success = NativeMethods.lou_backTranslate(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, typeFormBuffer, spacing, outputPosition, inputPosition, ref cursorPosition, mode) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return new TranslatedString
        {
            Output = ConvertUCSOutputBufferToString(outputBuffer, outputLength),
            CursorPosition = cursorPosition,
            InputPosition = inputPosition,
            OutputPosition = outputPosition,
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
            ThrowIfDisposed();
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
            ThrowIfDisposed();
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
    /// instead would leave a window for Dispose to free the tables between the check and the call.
    /// </remarks>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposedValue, this);
    }

    /// <summary>
    /// Frees everything liblouis has allocated.
    /// </summary>
    /// <remarks>
    /// This is process-global teardown, not the release of a per-instance resource: lou_free
    /// walks and frees the translation and display table chains that every caller shares. It
    /// therefore takes the same lock as every other native call - freeing those chains while
    /// another thread is translating is a use-after-free, which shows up as anything from a
    /// nonsense "no mapping for dot pattern" error to a crash.
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        lock (NativeLock)
        {
            if (disposedValue)
            {
                return;
            }

            NativeMethods.lou_free();

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);

        // There is no finalizer to suppress, but a derived type could introduce one and would
        // otherwise have to re-implement IDisposable just to make this call.
        GC.SuppressFinalize(this);
    }
}
