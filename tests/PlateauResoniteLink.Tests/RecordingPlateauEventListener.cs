using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;

using PlateauResoniteLink.Diagnostics;

namespace PlateauResoniteLink.Tests;

internal sealed class RecordingPlateauEventListener : EventListener
{
    private readonly EventLevel level;
    private readonly Action<string>? onMessage;
    private readonly string runId;
    private readonly IDisposable runScope;

    public RecordingPlateauEventListener(Action<string>? onMessage = null, EventLevel level = EventLevel.Verbose, string? runId = null)
    {
        this.onMessage = onMessage;
        this.level = level;
        this.runId = runId ?? Guid.NewGuid().ToString("N");
        runScope = PlateauDiagnostics.BeginRun(this.runId);
        foreach (EventSource eventSource in EventSource.GetSources())
        {
            EnablePlateauEvents(eventSource);
        }
    }

    public List<string> Messages { get; } = [];

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        EnablePlateauEvents(eventSource);
    }

    private void EnablePlateauEvents(EventSource eventSource)
    {
        if (string.Equals(eventSource.Name, PlateauDiagnostics.EventSourceName, StringComparison.Ordinal))
        {
            EnableEvents(eventSource, level);
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

        string message = GetPayloadValue(eventData, "message");
        if (!string.IsNullOrWhiteSpace(message))
        {
            Messages.Add(message);
            onMessage?.Invoke(message);
        }
    }

    private static string GetPayloadValue(EventWrittenEventArgs eventData, string name)
    {
        if (eventData.PayloadNames is not { Count: > 0 } names || eventData.Payload is not { Count: > 0 } payload)
        {
            return string.Empty;
        }

        for (int index = 0; index < names.Count && index < payload.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return payload[index]?.ToString() ?? string.Empty;
            }
        }

        int fallbackIndex = name switch
        {
            "runId" => 0,
            "phase" => 1,
            "message" => 2,
            "exception" => 3,
            _ => -1,
        };
        if (fallbackIndex >= 0 && fallbackIndex < payload.Count)
        {
            return payload[fallbackIndex]?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    public new void Dispose()
    {
        runScope.Dispose();
        base.Dispose();
    }
}
