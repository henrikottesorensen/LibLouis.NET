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
    private readonly object _lock;

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

    static LibLouis()
    {
        Instance = new LibLouis();
    }

    private LibLouis()
    {
        _lock = new object();
        CharacterSize = NativeMethods.lou_charSize();
        LibLouisStringEncoder = CharacterSize switch
        {
            2 => Encoding.Unicode, // UTF-16LE, LibLouis uses UCS-2, but UTF-16 should be compatible for MOST of the range.
            4 => Encoding.UTF32,   // UTF-32 is a direct mapping of UCS-4.
            _ => throw new NotImplementedException($"Liblouis is a character size of {CharacterSize}!?"),
        };

        // Register managed log callback, so we can give reasonable exception messages.
        NativeMethods.lou_registerLogCallback(LogCallback);
    }

    // https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/unmanaged
    ~LibLouis()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    private ILogger _logger = NullLogger.Instance;
    private bool disposedValue;

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

        lock (_lock)
        {
            _logger = logger;
            NativeMethods.lou_setLogLevel(LogLevel.All);
            NativeMethods.lou_registerLogCallback(LogCallback);
        }
    }

    private void LogCallback(LogLevel level, string message)
    {
        Microsoft.Extensions.Logging.LogLevel l = LogLevels[level];
        _lastLogMessage = message;

        if (_logger.IsEnabled(l))
        {
            _logger.Log(l, message);
        }
    }

    /// <summary>
    /// Returns version number of the native liblouis library.
    /// </summary>
    public string Version
    {
        get
        {
            string version = NativeMethods.lou_version();
            return version;
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
            lock (_lock)
            {
                return NativeMethods.lou_getDataPath();
            }
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
            lock (_lock)
            {
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

        lock (_lock)
        {
            return NativeMethods.lou_findTable(query);
        }
    }

    /// <summary>
    /// This function must be called prior to lou_findTable. It parses, analyzes and indexes all specified tables. Tables that contain invalid metadata are ignored. 
    /// </summary>
    /// <param name="tables">tables must be an IEnumerable of file names.</param>
    public void IndexTables(IEnumerable<string> tables)
    {
        lock (_lock)
        {
            NativeMethods.lou_indexTables(tables.ToArray());
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

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(input.Length);

        string tables = string.Join(',', tableList);
        bool success;

        lock (_lock)
        {
            success = NativeMethods.lou_dotsToChar(tables, inputBuffer, outputBuffer, input.Length, TranslationMode.Regular) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return ConvertUCSOutputBufferToString(outputBuffer, input.Length);
    }

    /// <summary>
    /// This function takes a string in input consisting of dot patterns and converts it to a string in output consisting of characters according to the specifications in tableList.
    /// </summary>
    /// <param name="input">The dot patterns in input can be in either liblouis format or Unicode braille.</param>
    /// <returns></returns>
    public string CharactersToDots(IEnumerable<string> tableList, string input)
    {
        ArgumentNullException.ThrowIfNull(input, nameof(input));

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(input.Length);
        
        string tables = string.Join(',', tableList);

        bool success;

        lock (_lock)
        {
            success = NativeMethods.lou_charToDots(tables, inputBuffer, outputBuffer, input.Length, TranslationMode.Regular) > 0;
        }

        if (!success)
        {
            throw new LibLouisException($"String translation failed: {_lastLogMessage}");
        }

        return ConvertUCSOutputBufferToString(outputBuffer, input.Length);

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

        int inputLength = input.Length + 1;
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);

        string tables = string.Join(',', tableList);
        bool success;

        lock (_lock)
        {
            success = NativeMethods.lou_translate(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, formtype, spacing, outputPosition, inputPosition, ref cursorPosition, mode) > 0;
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

        int inputLength = input.Length + 1;
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);

        string tables = string.Join(',', tableList);
        bool success;

        lock (_lock)
        {
            success = NativeMethods.lou_translateString(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, formtype, spacing, mode) > 0;
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

        int inputLength = input.Length + 1;
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);

        string tables = string.Join(',', tableList);
        bool success;

        lock (_lock)
        {
            success = NativeMethods.lou_backTranslate(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, formtype, spacing, outputPosition, inputPosition, ref cursorPosition, mode) > 0;
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

        int inputLength = input.Length + 1;
        int outputBufferLength = outputLength;

        byte[] inputBuffer = PrepareUCSInputBuffer(input);
        byte[] outputBuffer = PrepareUCSOutputBuffer(outputBufferLength);

        string tables = string.Join(',', tableList);
        bool success;

        lock (_lock)
        {
            success = NativeMethods.lou_backTranslateString(tables, inputBuffer, ref inputLength, outputBuffer, ref outputLength, formtype, spacing, mode) > 0;
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
    /// <param name="input"></param>
    /// <param name="mode"></param>
    /// <returns></returns>
    /// <exception cref="LibLouisException"></exception>
    public string Hyphenate(IEnumerable<string> tableList, string input, TranslationMode mode)
    {
        ArgumentNullException.ThrowIfNull(tableList);
        ArgumentNullException.ThrowIfNullOrEmpty(nameof(input));

        string tables = string.Join(',', tableList);
        string hyphens = new('\0', input.Length + 1);

        byte[] inputBuffer = PrepareUCSInputBuffer(input);

        bool success;
       
        lock (_lock)
        {
            success = NativeMethods.lou_hyphenate(tables, inputBuffer, input.Length + 1, ref hyphens, mode) > 0; 
        }
        
        if (!success)
        {
            throw new LibLouisException($"Hyphenation failed {_lastLogMessage}");
        }

        return hyphens;
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

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects)
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
            NativeMethods.lou_free();

            // Set large fields to null
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
