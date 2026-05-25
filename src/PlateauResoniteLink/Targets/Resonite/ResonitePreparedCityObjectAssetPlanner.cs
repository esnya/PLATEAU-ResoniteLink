using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record PreparedCityObjectAssetPlan(
    PlannedGeometryAsset GeometryAsset,
    PlannedSceneMaterialPlan Materials,
    double GeometryAssetSeconds,
    double MaterialSeconds);

internal interface IResonitePreparedCityObjectAssetPlanner
{
    Task<PreparedCityObjectAssetPlan> PlanAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        PreparedCityObject preparedCityObject,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResonitePreparedCityObjectAssetPlanner(
    IResonitePreparedTextureUploader textureUploader,
    IResoniteGeometryAssetPlanner geometryAssetPlanner,
    IResoniteSceneMaterialPlanComposer sceneMaterialPlanComposer) : IResonitePreparedCityObjectAssetPlanner
{
    private readonly IResonitePreparedTextureUploader textureUploader =
        textureUploader ?? throw new ArgumentNullException(nameof(textureUploader));
    private readonly IResoniteGeometryAssetPlanner geometryAssetPlanner =
        geometryAssetPlanner ?? throw new ArgumentNullException(nameof(geometryAssetPlanner));
    private readonly IResoniteSceneMaterialPlanComposer sceneMaterialPlanComposer =
        sceneMaterialPlanComposer ?? throw new ArgumentNullException(nameof(sceneMaterialPlanComposer));

    public async Task<PreparedCityObjectAssetPlan> PlanAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        PreparedCityObject preparedCityObject,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(preparedCityObject);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using CancellationTokenSource importStepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay =
            CreatePreparedTerrainTextureDataByOverlay(preparedCityObject);
        Task<ResoniteUploadedTextureAssetSet> uploadedTextureAssetsTask = textureUploader.UploadAsync(
            state,
            routedClient,
            preparedCityObject,
            importStepCancellation.Token);
        Stopwatch geometryStopwatch = Stopwatch.StartNew();
        Task<PlannedGeometryAsset> geometryPlanningTask = geometryAssetPlanner.PlanAsync(
            routedClient,
            cityObject,
            preparedCityObject,
            preparedTerrainTextureDataByOverlay,
            progressReporter,
            importStepCancellation.Token);
        Stopwatch materialStopwatch = new();
        Task<PlannedSceneMaterialPlan>? materialPlanningTask = null;
        Action<string> reportProgress = progressReporter ?? (_ => { });
        try
        {
            ResoniteUploadedTextureAssetSet uploadedTextureAssets = await uploadedTextureAssetsTask;
            materialStopwatch.Start();
            materialPlanningTask = sceneMaterialPlanComposer.ComposeAsync(
                state,
                routedClient,
                cityObject,
                uploadedTextureAssets.TextureUrisByPayload,
                uploadedTextureAssets.TerrainTextureUrisByOverlay,
                uploadedTextureAssets.TerrainTexturePropertyBlockComponentsByMeshCode,
                reportProgress,
                importStepCancellation.Token);
            PlannedSceneMaterialPlan plannedMaterials = await materialPlanningTask;
            materialStopwatch.Stop();

            reportProgress($"Preparing geometry assets ({PreparedConstructionGeometryFormatter.Describe(preparedCityObject.Geometry)}).");
            PlannedGeometryAsset plannedGeometryAsset = await geometryPlanningTask;
            geometryStopwatch.Stop();
            return new PreparedCityObjectAssetPlan(
                plannedGeometryAsset,
                plannedMaterials,
                geometryStopwatch.Elapsed.TotalSeconds,
                materialStopwatch.Elapsed.TotalSeconds);
        }
        catch
        {
            await importStepCancellation.CancelAsync();
            IEnumerable<Task> tasksToObserve = materialPlanningTask is null
                ? [uploadedTextureAssetsTask, geometryPlanningTask]
                : [uploadedTextureAssetsTask, materialPlanningTask, geometryPlanningTask];
            await ObserveTaskFailuresAsync(tasksToObserve);
            throw;
        }
    }

    private static Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> generatedTerrainTexturesByOverlay = [];
        foreach (PreparedTextureReference texture in preparedCityObject.Textures)
        {
            if (texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            {
                generatedTerrainTexturesByOverlay.TryAdd(texture.TerrainOverlay, texture.GeneratedTerrainTexture);
            }
        }

        return generatedTerrainTexturesByOverlay;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup should observe and suppress orphaned import task failures after the primary send failure.")]
    private static async Task ObserveTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static Task ObserveTaskFailuresAsync(IEnumerable<Task> tasks)
    {
        return Task.WhenAll(tasks.Select(ObserveTaskFailureAsync));
    }
}
