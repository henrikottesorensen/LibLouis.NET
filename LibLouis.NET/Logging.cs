using System;

namespace LibLouis.NET;

/// <remarks>
/// These change the same global liblouis state that <see cref="LibLouis"/> uses, so they take the
/// same lock. Setting the callback or the log level while another thread is inside a translation
/// is otherwise an unsynchronised write to state liblouis reads as it logs.
/// </remarks>
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

        lock (LibLouis.NativeLock)
        {
            _callback = value;
            NativeMethods.lou_registerLogCallback(_callback);
        }
    }

    private static LogLevel _logLevel = LogLevel.Off;

    public static LogLevel LogLevel
    {
        get
        {
            lock (LibLouis.NativeLock)
            {
                return _logLevel;
            }
        }
        set
        {
            lock (LibLouis.NativeLock)
            {
                _logLevel = value;
                NativeMethods.lou_setLogLevel(value);
            }
        }
    }

    public static void DebugLogCallback(LogLevel level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"{level}: {message}");
    }
}
