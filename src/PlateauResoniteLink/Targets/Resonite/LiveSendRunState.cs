using System.Collections.Concurrent;
using System.Threading;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class LiveSendRunState
{
    public required LiveSendRunContext Context { get; init; }

    public required LiveSendProgressSink Progress { get; init; }

    public required CommonMaterialAssetCache Materials { get; init; }

    public required TerrainTextureAssetCache TerrainTextures { get; init; }

    public required ResoniteSharedSlotIndex Placement { get; init; }

    public required LiveSendExecutionRuntime Runtime { get; init; }

    public int GsiFallbackLicenseEnsured;

    public required SemaphoreSlim GsiFallbackLicenseGate { get; init; }

    public required ConcurrentDictionary<string, int> DemSourceUseCounts { get; init; }
}
