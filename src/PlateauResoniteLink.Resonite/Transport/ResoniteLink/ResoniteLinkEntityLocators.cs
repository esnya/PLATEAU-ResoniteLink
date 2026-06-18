using System;
using System.Collections.Generic;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Transport.ResoniteLink;

internal readonly record struct ResoniteTransportSlotLocator
{
    public static ResoniteTransportSlotLocator Root { get; } = new("Root");

    public ResoniteTransportSlotLocator(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }

    public bool IsRoot => string.Equals(Value, Root.Value, StringComparison.Ordinal);
}

internal readonly record struct ResoniteTransportComponentLocator
{
    public ResoniteTransportComponentLocator(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }
}

internal readonly record struct ResoniteTransportSlotCreationResult(ResoniteTransportSlotLocator Slot);

internal readonly record struct ResoniteTransportComponentCreationResult(ResoniteTransportComponentLocator Component);

internal sealed class ResoniteComponentUpdate
{
    public required ResoniteTransportComponentLocator Component { get; init; }

    public required IReadOnlyDictionary<string, Member> Members { get; init; }
}
