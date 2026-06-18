using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading;
using System.Text.RegularExpressions;

namespace PlateauResoniteLink.Diagnostics;

public static class PlateauDiagnostics
{
    public const string EventSourceName = "PlateauResoniteLink";
    public const string ActivitySourceName = "PlateauResoniteLink";
    public const string MeterName = "PlateauResoniteLink";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Regex TemplateTokenPattern = new(@"\{(?<name>[^}:]+)(:(?<format>[^}]+))?\}", RegexOptions.Compiled);
    private static readonly AsyncLocal<string?> CurrentRunId = new();

    public static IDisposable BeginRun(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return new DiagnosticRunScope(runId);
    }

    public static void Progress(string messageTemplate, params object?[] args)
    {
        if (PlateauEventSource.Log.IsInformationalEnabled())
        {
            PlateauEventSource.Log.Progress(CurrentRunId.Value ?? string.Empty, "import", FormatMessage(messageTemplate, args));
        }
    }

    public static void Warning(string messageTemplate, params object?[] args)
    {
        if (PlateauEventSource.Log.IsWarningEnabled())
        {
            PlateauEventSource.Log.Warning(CurrentRunId.Value ?? string.Empty, "warning", FormatMessage(messageTemplate, args));
        }
    }

    public static void Warning(Exception exception, string messageTemplate, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (PlateauEventSource.Log.IsWarningEnabled())
        {
            PlateauEventSource.Log.Warning(CurrentRunId.Value ?? string.Empty, "warning", $"{FormatMessage(messageTemplate, args)} {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void Verbose(string messageTemplate, params object?[] args)
    {
        if (PlateauEventSource.Log.IsVerboseEnabled())
        {
            PlateauEventSource.Log.Verbose(CurrentRunId.Value ?? string.Empty, "detail", FormatMessage(messageTemplate, args));
        }
    }

    public static void Error(Exception exception, string messageTemplate, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (PlateauEventSource.Log.IsErrorEnabled())
        {
            PlateauEventSource.Log.Error(CurrentRunId.Value ?? string.Empty, "error", FormatMessage(messageTemplate, args), exception.ToString());
        }
    }

    public static Activity? StartActivity(string name)
    {
        return ActivitySource.StartActivity(name);
    }

    private static string FormatMessage(string messageTemplate, object?[] args)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);
        if (args.Length == 0)
        {
            return messageTemplate;
        }

        int index = 0;
        return TemplateTokenPattern.Replace(
            messageTemplate,
            match =>
            {
                if (index >= args.Length)
                {
                    return match.Value;
                }

                object? value = args[index++];
                string? format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
                if (value is IFormattable formattable)
                {
                    return formattable.ToString(format, CultureInfo.InvariantCulture);
                }

                return value?.ToString() ?? string.Empty;
            });
    }

    private sealed class DiagnosticRunScope : IDisposable
    {
        private readonly string? previousRunId;
        private bool disposed;

        public DiagnosticRunScope(string runId)
        {
            previousRunId = CurrentRunId.Value;
            CurrentRunId.Value = runId;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CurrentRunId.Value = previousRunId;
            disposed = true;
        }
    }
}
