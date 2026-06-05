using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class LiveSendProgressSink
{
    public int AttemptedCityObjectCount;

    public int ProcessedCityObjectCount;

    public int FailedCityObjectCount;

    public int FirstQueuedCityObjectLogged;

    public int FirstPreparedCityObjectLogged;

    public int FirstImportedCityObjectLogged;

    public int FirstCityObjectPreparationStartedLogged;

    public int FirstCityObjectStreamingStartedLogged;

    public int FirstCityObjectDequeuedLogged;

    public void Reset()
    {
        AttemptedCityObjectCount = 0;
        ProcessedCityObjectCount = 0;
        FailedCityObjectCount = 0;
        FirstQueuedCityObjectLogged = 0;
        FirstPreparedCityObjectLogged = 0;
        FirstImportedCityObjectLogged = 0;
        FirstCityObjectPreparationStartedLogged = 0;
        FirstCityObjectStreamingStartedLogged = 0;
        FirstCityObjectDequeuedLogged = 0;
    }
}

internal sealed class CommonMaterialAssetCache
{
    public ConcurrentDictionary<string, Task> CommonMaterialFamilyWarmupTasks { get; } = new(StringComparer.Ordinal);

    public ResoniteCommonMaterialAssetAccumulator CommonMaterialAssets { get; } = new();

    public AsyncInFlightResultCache<BundledTextureImportKey, Uri> BundledTextureImportTasks { get; } = new();
}

internal sealed class TerrainTextureAssetCache
{
    public AsyncInFlightResultCache<string, SharedTerrainTextureAsset> AssetsByMeshCode { get; } = new();
}

internal sealed record SharedTerrainTextureAsset(
    Uri TextureUri,
    CreatedComponent TextureComponent,
    CreatedComponent MainTexturePropertyBlockComponent);

internal sealed record LiveSendRunPlan
{
    public LiveSendRunPlan(
        ResoniteSceneSetupInfo SetupInfo,
        string ResolvedWorkRoot,
        ResoniteLocalOrigin RequestLocalOrigin,
        IReadOnlyDictionary<string, string> SourceFileSlotNamesByRelativePath,
        ResoniteImportBudgetProfile ResourceBudget,
        LiveSendQueuePlan Queue,
        bool MeshBakeEnabled)
    {
        ArgumentNullException.ThrowIfNull(SetupInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(ResolvedWorkRoot);
        ArgumentNullException.ThrowIfNull(SourceFileSlotNamesByRelativePath);
        ArgumentNullException.ThrowIfNull(ResourceBudget);
        ArgumentNullException.ThrowIfNull(Queue);

        this.SetupInfo = SetupInfo;
        this.ResolvedWorkRoot = ResolvedWorkRoot;
        this.RequestLocalOrigin = RequestLocalOrigin;
        this.SourceFileSlotNamesByRelativePath = SourceFileSlotNamesByRelativePath;
        this.ResourceBudget = ResourceBudget;
        this.Queue = Queue;
        this.MeshBakeEnabled = MeshBakeEnabled;
    }

    public ResoniteSceneSetupInfo SetupInfo { get; }

    public string ResolvedWorkRoot { get; }

    public ResoniteLocalOrigin RequestLocalOrigin { get; }

    public IReadOnlyDictionary<string, string> SourceFileSlotNamesByRelativePath { get; }

    public ResoniteImportBudgetProfile ResourceBudget { get; }

    public LiveSendQueuePlan Queue { get; }

    public bool MeshBakeEnabled { get; }
}

internal sealed record LiveSendQueuePlan
{
    public LiveSendQueuePlan(
        int ConnectionCount,
        int QueueCapacity,
        long MemoryBudgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MemoryBudgetBytes, 1);

        this.ConnectionCount = ConnectionCount;
        this.QueueCapacity = QueueCapacity;
        this.MemoryBudgetBytes = MemoryBudgetBytes;
    }

    public int ConnectionCount { get; }

    public int QueueCapacity { get; }

    public long MemoryBudgetBytes { get; }
}

internal sealed record LiveSendRunContext(
    LiveSendRunPlan Plan,
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    CompositeCityObjectBaker? CityObjectBaker);

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

    public required ConcurrentDictionary<TerrainTextureSource, int> DemSourceUseCounts { get; init; }
}
