using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;

using PlateauResoniteLink.Diagnostics;

namespace PlateauResoniteLink.Cli;

internal sealed class CliProgressEventListener : EventListener
{
    private readonly TextWriter writer;
    private readonly bool verbose;
    private readonly string runId;

    public CliProgressEventListener(TextWriter writer, bool verbose, string runId)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.verbose = verbose;
        this.runId = runId ?? throw new ArgumentNullException(nameof(runId));
        foreach (EventSource eventSource in EventSource.GetSources())
        {
            EnablePlateauEvents(eventSource);
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        EnablePlateauEvents(eventSource);
    }

    private void EnablePlateauEvents(EventSource eventSource)
    {
        if (string.Equals(eventSource.Name, PlateauDiagnostics.EventSourceName, StringComparison.Ordinal))
        {
            EnableEvents(eventSource, verbose ? EventLevel.Verbose : EventLevel.Informational);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        string eventRunId = GetPayloadValue(eventData, "runId");
        if (!string.Equals(eventRunId, runId, StringComparison.Ordinal))
        {
            return;
        }

        string phase = GetPayloadValue(eventData, "phase");
        string message = GetPayloadValue(eventData, "message");
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string level = eventData.Level switch
        {
            EventLevel.Critical => "crit",
            EventLevel.Error => "fail",
            EventLevel.Warning => "warn",
            EventLevel.Informational => "info",
            EventLevel.Verbose => "dbug",
            _ => eventData.Level.ToString(),
        };
        string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        string category = string.IsNullOrWhiteSpace(phase) ? "progress" : phase;

        lock (writer)
        {
            writer.WriteLine($"[{timestamp}] {level,-5} {category}: {message}");
            if (verbose)
            {
                string exception = GetPayloadValue(eventData, "exception");
                if (!string.IsNullOrWhiteSpace(exception))
                {
                    writer.WriteLine(exception);
                }
            }
        }
    }

    private static string GetPayloadValue(EventWrittenEventArgs eventData, string name)
    {
        if (eventData.PayloadNames is not { Count: > 0 } names || eventData.Payload is not { Count: > 0 } payload)
        {
            return string.Empty;
        }

        int index = IndexOf(names, name);
        if (index < 0)
        {
            index = name switch
            {
                "runId" => 0,
                "phase" => 1,
                "message" => 2,
                "exception" => 3,
                _ => -1,
            };
        }

        if (index < 0 || index >= payload.Count)
        {
            return string.Empty;
        }

        return payload[index]?.ToString() ?? string.Empty;
    }

    private static int IndexOf(ReadOnlyCollection<string> names, string name)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
