using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Diagnostics;

internal static class PlateauLoggerExtensions
{
    [SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "CLI import diagnostics are user-facing progress logs with varied message shapes.")]
    [SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging arguments", Justification = "The helper checks the configured level before forwarding structured arguments.")]
    [SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Call sites pass stable templates; this helper centralizes level gating.")]
    public static void WriteDebug(this ILogger logger, string message, params object?[] args)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(message, args);
        }
    }

    [SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "CLI import diagnostics are user-facing progress logs with varied message shapes.")]
    [SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging arguments", Justification = "The helper checks the configured level before forwarding structured arguments.")]
    [SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Call sites pass stable templates; this helper centralizes level gating.")]
    public static void WriteInformation(this ILogger logger, string message, params object?[] args)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(message, args);
        }
    }

    [SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "CLI import diagnostics are user-facing progress logs with varied message shapes.")]
    [SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging arguments", Justification = "The helper checks the configured level before forwarding structured arguments.")]
    [SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Call sites pass stable templates; this helper centralizes level gating.")]
    public static void WriteWarning(this ILogger logger, string message, params object?[] args)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(message, args);
        }
    }

    [SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "CLI import diagnostics are user-facing progress logs with varied message shapes.")]
    [SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging arguments", Justification = "The helper checks the configured level before forwarding structured arguments.")]
    [SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Call sites pass stable templates; this helper centralizes level gating.")]
    public static void WriteError(
        this ILogger logger,
        Exception exception,
        string message,
        params object?[] args)
    {
        if (logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(exception, message, args);
        }
    }
}
