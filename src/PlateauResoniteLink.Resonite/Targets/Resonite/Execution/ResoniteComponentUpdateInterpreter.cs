using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

internal sealed class ResoniteComponentUpdateInterpreter
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Keep component update emission behind an instance interpreter so the update boundary stays injectable and explicit.")]
    public Task ApplyAsync(
        IResoniteLinkClient client,
        string componentId,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(members);

        return client.UpdateComponentAsync(
            new ResoniteComponentUpdate
            {
                Component = new ResoniteTransportComponentLocator(componentId),
                Members = members.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value,
                    StringComparer.Ordinal),
            },
            cancellationToken);
    }
}
