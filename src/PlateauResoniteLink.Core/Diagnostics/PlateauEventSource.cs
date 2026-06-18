using System.Diagnostics.Tracing;

namespace PlateauResoniteLink.Core.Diagnostics;

[EventSource(Name = PlateauDiagnostics.EventSourceName)]
internal sealed class PlateauEventSource : EventSource
{
    public static readonly PlateauEventSource Log = new();

    private PlateauEventSource()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void Progress(string runId, string phase, string message)
    {
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            WriteEvent(1, runId, phase, message);
        }
    }

    [Event(2, Level = EventLevel.Warning)]
    public void Warning(string runId, string phase, string message)
    {
        if (IsEnabled(EventLevel.Warning, EventKeywords.None))
        {
            WriteEvent(2, runId, phase, message);
        }
    }

    [Event(3, Level = EventLevel.Verbose)]
    public void Verbose(string runId, string phase, string message)
    {
        if (IsEnabled(EventLevel.Verbose, EventKeywords.None))
        {
            WriteEvent(3, runId, phase, message);
        }
    }

    [Event(4, Level = EventLevel.Error)]
    public void Error(string runId, string phase, string message, string exception)
    {
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            WriteEvent(4, runId, phase, message, exception);
        }
    }

    [NonEvent]
    public bool IsInformationalEnabled()
    {
        return IsEnabled(EventLevel.Informational, EventKeywords.None);
    }

    [NonEvent]
    public bool IsWarningEnabled()
    {
        return IsEnabled(EventLevel.Warning, EventKeywords.None);
    }

    [NonEvent]
    public bool IsVerboseEnabled()
    {
        return IsEnabled(EventLevel.Verbose, EventKeywords.None);
    }

    [NonEvent]
    public bool IsErrorEnabled()
    {
        return IsEnabled(EventLevel.Error, EventKeywords.None);
    }
}
