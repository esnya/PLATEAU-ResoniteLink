using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteImportedObjectUnitStreamQueueWriter
{
    Task QueueAsync(
        LiveSendRunState state,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        IResoniteLinkClient routedClient,
        int connectionCount,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteImportedObjectUnitStreamQueueWriter(
    IResoniteCityObjectQueueWriter cityObjectQueueWriter) : IResoniteImportedObjectUnitStreamQueueWriter
{
    private readonly IResoniteCityObjectQueueWriter cityObjectQueueWriter =
        cityObjectQueueWriter ?? throw new ArgumentNullException(nameof(cityObjectQueueWriter));

    public async Task QueueAsync(
        LiveSendRunState state,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        IResoniteLinkClient routedClient,
        int connectionCount,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(objectUnits);
        ArgumentNullException.ThrowIfNull(routedClient);

        await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
        {
            await cityObjectQueueWriter.QueueObjectUnitAsync(
                state,
                objectUnit,
                routedClient,
                connectionCount,
                progressReporter,
                cancellationToken);
        }
    }
}
