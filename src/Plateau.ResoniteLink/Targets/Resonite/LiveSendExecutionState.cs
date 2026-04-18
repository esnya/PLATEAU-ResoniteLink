using System.Collections.Concurrent;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed record LiveSendExecutionContext(
    SceneBootstrapInfo BootstrapInfo,
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    IPlateauDatasetContentSource DatasetContentSource,
    ResoniteLocalOrigin RequestLocalOrigin,
    IReadOnlyDictionary<string, string> CityGmlSlotNamesByRelativePath,
    CompositeCityObjectBaker? CityObjectBaker);

internal sealed class LiveSendProgressState
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

internal sealed class LiveSendMaterialState
{
    public ConcurrentDictionary<string, Task> CommonMaterialFamilyWarmupTasks { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Task<CreatedMaterialAsset>> CommonMaterialCreationTasks { get; } = new(StringComparer.Ordinal);
}

internal sealed class LiveSendExecutionRun
{
    public required LiveSendExecutionContext Context { get; init; }

    public required LiveSendProgressState Progress { get; init; }

    public required LiveSendMaterialState Materials { get; init; }

    public required ResoniteScenePlacementSession Placement { get; init; }

    public required AsyncCompletedResultCache<TextureImportCacheKey, Uri> ImportedTextureUriCache { get; init; }

    public required ResoniteLinkSceneBuilder.LiveSendExecutionRuntime Runtime { get; init; }
}
