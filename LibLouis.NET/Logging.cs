using System;

namespace LibLouis.NET;

public static class Logging
{
    /// <summary>
    /// Roots the delegate behind the function pointer liblouis holds. Callers routinely pass a
    /// method group, which would otherwise be collected while liblouis still calls it.
    /// </summary>
    private static NativeMethods.LoggingCallback? _callback;

    public static void SetCallback(NativeMethods.LoggingCallback value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _callback = value;
        NativeMethods.lou_registerLogCallback(_callback);
    }

    private static LogLevel _logLevel = LogLevel.Off;

    public static LogLevel LogLevel
    {
        get
        {
            return _logLevel;
        }
        set
        {
            _logLevel = value;
            NativeMethods.lou_setLogLevel(value);
        }
    }

    public static void DebugLogCallback(LogLevel level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"{level}: {message}");
    }
}
