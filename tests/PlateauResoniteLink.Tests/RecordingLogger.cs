using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Tests;

internal sealed class RecordingLogger(Action<string>? onMessage = null) : ILogger
{
    private sealed class Scope : IDisposable
    {
        public static readonly Scope Instance = new();

        public void Dispose()
        {
        }
    }

    public List<string> Messages { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        _ = state;
        return Scope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        _ = logLevel;
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _ = logLevel;
        _ = eventId;
        string message = formatter(state, exception);
        Messages.Add(message);
        onMessage?.Invoke(message);
    }
}

internal sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
        _ = provider;
    }

    public ILogger CreateLogger(string categoryName)
    {
        _ = categoryName;
        return logger;
    }

    public void Dispose()
    {
    }
}
