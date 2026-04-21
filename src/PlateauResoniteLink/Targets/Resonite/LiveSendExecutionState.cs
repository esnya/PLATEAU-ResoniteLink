using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendExecutionContext(
    SceneBootstrapInfo BootstrapInfo,
    CreatedSlot DatasetRootSlot,
    CompositeCityObjectBaker? CityObjectBaker);

internal sealed class LiveSendProgressSink
{
    public int AttemptedCityObjectCount;

    public int ProcessedCityObjectCount;

    public int FailedCityObjectCount;

    public int FirstQueuedCityObjectLogged;

    public int FirstPreparedCityObjectLogged;

    public int FirstBuiltCityObjectLogged;

    public int FirstCityObjectPreparationStartedLogged;

    public int FirstCommonMaterialPrepLogged;

    public int FirstCityObjectStreamingStartedLogged;

    public int FirstCityObjectDequeuedLogged;

    public void Reset()
    {
        AttemptedCityObjectCount = 0;
        ProcessedCityObjectCount = 0;
        FailedCityObjectCount = 0;
        FirstQueuedCityObjectLogged = 0;
        FirstPreparedCityObjectLogged = 0;
        FirstBuiltCityObjectLogged = 0;
        FirstCityObjectPreparationStartedLogged = 0;
        FirstCommonMaterialPrepLogged = 0;
        FirstCityObjectStreamingStartedLogged = 0;
        FirstCityObjectDequeuedLogged = 0;
    }
}

internal sealed class CommonMaterialAssetCache
{
    public ConcurrentDictionary<string, Task> CommonMaterialFamilyWarmupTasks { get; } = new(StringComparer.Ordinal);

    public AsyncInFlightResultCache<string, CreatedMaterialAsset> CommonMaterialCreationTasks { get; } = new();

    public required IReadOnlySet<string> BootstrapKnownMaterialKeys { get; init; }
}

internal sealed record LiveSendRunPlan(
    SceneBootstrapInfo BootstrapInfo,
    string ResolvedWorkRoot,
    ResoniteLocalOrigin RequestLocalOrigin,
    IReadOnlyDictionary<string, string> SourceFileSlotNamesByRelativePath,
    ResoniteImportBudgetProfile ResourceBudget,
    LiveSendQueuePlan Queue,
    bool MeshBakeEnabled);

internal sealed record LiveSendQueuePlan(
    int ConnectionCount,
    int QueueCapacity,
    long MemoryBudgetBytes);

internal sealed record LiveSendRunContext(
    LiveSendRunPlan Plan,
    CreatedSlot DatasetRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    CompositeCityObjectBaker? CityObjectBaker);

internal sealed class LiveSendRunState
{
    public required LiveSendRunContext Context { get; init; }

    public required LiveSendProgressSink Progress { get; init; }

    public required CommonMaterialAssetCache Materials { get; init; }

    public required ResoniteSharedSlotIndex Placement { get; init; }

    public required AsyncCompletedResultCache<TextureImportCacheKey, Uri> ImportedTextureUriCache { get; init; }

    public required LiveSendExecutionRuntime Runtime { get; init; }

    public int GsiFallbackLicenseEnsured;

    public required SemaphoreSlim GsiFallbackLicenseGate { get; init; }

    public required ConcurrentDictionary<string, int> DemSourceUseCounts { get; init; }
}
