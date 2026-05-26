using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectPreparation
{
    Task<PreparedCityObject> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken callerCancellationToken);
}

internal sealed class ResoniteQueuedCityObjectPreparation(
    IResoniteQueuedTexturePreparer texturePreparer) : IResoniteQueuedCityObjectPreparation
{
    private readonly IResoniteQueuedTexturePreparer texturePreparer =
        texturePreparer ?? throw new ArgumentNullException(nameof(texturePreparer));

    public async Task<PreparedCityObject> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken callerCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectPreparationStartedLogged, 1, 0) == 0)
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"City object preparation started after {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"mesh='{cityObject.ActualMeshCode}'."));
        }

        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            state.Runtime.ProcessingCancellationToken);
        return await PrepareCityObjectAsync(
            state,
            routedClient,
            cityObject,
            diagnostics,
            progressReporter,
            linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);

        if (cityObject.Geometry is ResoniteTriangleMeshGeometry triangleGeometry)
        {
            ResoniteCityObjectPreparation.ValidateTriangleMeshBindingsForImport(cityObject, triangleGeometry.Mesh);
        }
        else if (cityObject.Geometry is ResoniteDynamicTerrainGeometry dynamicTerrain)
        {
            ResoniteCityObjectPreparation.ValidateTriangleMeshBindingsForImport(cityObject, dynamicTerrain.StaticMesh.Mesh);
        }

        Task<PreparedConstructionGeometry> geometryPreparationTask = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, triangleMesh.Mesh),
                cancellationToken),
            ResoniteTerrainGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedTerrainGridGeometry(heightMap, ResoniteCityObjectPreparation.PrepareTerrainGridDisplacementTexture(heightMap)),
                cancellationToken),
            ResoniteDynamicTerrainGeometry dynamicTerrain => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedDynamicTerrainGeometry(
                    ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, dynamicTerrain.StaticMesh.Mesh),
                    new PreparedTerrainGridGeometry(
                        dynamicTerrain.GridMesh,
                        ResoniteCityObjectPreparation.PrepareTerrainGridDisplacementTexture(dynamicTerrain.GridMesh))),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await texturePreparer.PrepareAsync(
            state,
            routedClient,
            cityObject,
            progressReporter,
            cancellationToken);
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask.WaitAsync(cancellationToken);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay = preparedTextures
            .Where(static texture => texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.GeneratedTerrainTexture!);
        cityObject = ResoniteCityObjectPreparation.ApplyTerrainTextureCanvasUv(
            cityObject,
            preparedTerrainTextureDataByOverlay,
            clampCanvasUv: ResonitePackageSemantics.IsDemPackage(cityObject.PackageName));
        if (cityObject.Geometry is ResoniteTriangleMeshGeometry resolvedTriangleMesh
            && preparedGeometry is PreparedTriangleMeshGeometry)
        {
            preparedGeometry = ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, resolvedTriangleMesh.Mesh);
        }
        else if (cityObject.Geometry is ResoniteDynamicTerrainGeometry resolvedDynamicTerrain
            && preparedGeometry is PreparedDynamicTerrainGeometry preparedDynamicTerrain)
        {
            preparedGeometry = preparedDynamicTerrain with
            {
                StaticMesh = ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, resolvedDynamicTerrain.StaticMesh.Mesh),
            };
        }

        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref state.Progress.FirstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                    + $"after scene start {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} "
                    + $"(textures={preparedTextures.Length}, geometry={PreparedConstructionGeometryFormatter.Describe(preparedGeometry)})."));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
