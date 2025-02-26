namespace LibLouis.NET;

public static class Logging
{
    public static void SetCallback(NativeMethods.LoggingCallback value)
    {
        NativeMethods.lou_registerLogCallback(value);
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
