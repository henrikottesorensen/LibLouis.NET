using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace LibLouis.NET.Test;

/// <summary>
/// Captures everything liblouis logs, so tests can assert on what the native side reported.
/// </summary>
internal sealed class CollectingLogger : ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // LogLevel is qualified throughout: in this namespace the unqualified name binds to
    // LibLouis.NET.LogLevel, the native enum, not the Microsoft.Extensions.Logging one.
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}
