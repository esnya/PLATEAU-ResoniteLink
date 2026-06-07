using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunRuntimeComponents(
    TerrainTextureAssetCache TerrainTextures,
    LiveSendExecutionRuntime Runtime,
    SemaphoreSlim GsiFallbackLicenseGate,
    ConcurrentDictionary<TerrainTextureSource, int> DemSourceUseCounts);

internal static class LiveSendRunRuntimeComponentsFactory
{
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to LiveSendRunState and released by ResoniteLiveSendRunResourceReleaser.")]
    public static LiveSendRunRuntimeComponents Create(
        LiveSendQueuePlan queuePlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queuePlan);

        return new LiveSendRunRuntimeComponents(
            new TerrainTextureAssetCache(),
            new LiveSendExecutionRuntime(queuePlan, cancellationToken),
            new SemaphoreSlim(1, 1),
            new ConcurrentDictionary<TerrainTextureSource, int>());
    }
}
