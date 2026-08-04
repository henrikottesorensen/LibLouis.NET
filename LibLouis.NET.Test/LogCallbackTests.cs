using System;

using Microsoft.Extensions.Logging;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// liblouis keeps the function pointer it is handed by lou_registerLogCallback and calls it for
/// the rest of the process's life. The managed delegate behind that pointer therefore has to stay
/// alive for just as long: the marshalling stub only keeps it alive for the duration of the
/// registration call itself.
/// </summary>
public class LogCallbackTests
{
    /// <summary>
    /// Forces collections between registering the callback and provoking a native log message.
    /// If nothing roots the delegate, the pointer liblouis holds is dangling by then.
    /// </summary>
    [Fact]
    public void Logger_StillReceivesMessagesAfterGarbageCollection()
    {
        CollectingLogger logger = new();
        LibLouis.Instance.Logger = logger;

        for (int i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        // Any failing call makes liblouis log; a table that cannot be compiled is the simplest.
        Assert.Throws<LibLouisException>(
            () => LibLouis.Instance.Translate(
                ["no-such-table-at-all.ctb"], "x", 8, null, null, TranslationMode.Regular));

        Assert.NotEmpty(logger.Messages);
    }

    /// <summary>
    /// The callback runs on a native stack. An exception thrown out of it cannot be handled by
    /// liblouis and tears the process down, so an unmapped level must not throw.
    /// </summary>
    [Fact]
    public void LogCallback_SurvivesALevelItDoesNotKnow()
    {
        CollectingLogger logger = new();
        LibLouis.Instance.Logger = logger;

        // 12345 is not one of the logLevels values liblouis defines.
        NativeMethods.LoggingCallback callback = GetRegisteredCallback();

        callback((LogLevel)12345, "message at an unknown level");
    }

    /// <summary>
    /// Reaches the delegate the wrapper registered, so the test calls exactly what liblouis calls.
    /// </summary>
    private static NativeMethods.LoggingCallback GetRegisteredCallback()
    {
        object? field = typeof(LibLouis)
            .GetField("_logCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(LibLouis.Instance);

        Assert.NotNull(field);

        return (NativeMethods.LoggingCallback)field;
    }
}
