using System;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Cli;

internal sealed class CliTextWriterLoggerProvider(
    TextWriter writer,
    LogLevel minimumLevel) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new CliTextWriterLogger(writer, categoryName, minimumLevel);
    }

    public void Dispose()
    {
    }
}

internal sealed class CliTextWriterLogger(
    TextWriter writer,
    string categoryName,
    LogLevel minimumLevel) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= minimumLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _ = eventId;
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        string level = FormatLevel(logLevel);
        string message = formatter(state, exception);
        lock (writer)
        {
            writer.WriteLine($"[{timestamp}] {level,-5} {categoryName}: {message}");
            if (exception is not null)
            {
                writer.WriteLine(exception);
            }
        }
    }

    private static string FormatLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => logLevel.ToString(),
        };
    }
}
