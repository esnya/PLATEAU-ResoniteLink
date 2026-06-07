using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteQueuedCityObjectPreparation(
    ResoniteQueuedTexturePreparer texturePreparer)
{
    private readonly ResoniteQueuedTexturePreparer texturePreparer =
        texturePreparer ?? throw new ArgumentNullException(nameof(texturePreparer));

    public async Task<PreparedCityObject> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        ILogger logger,
        CancellationToken callerCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectPreparationStartedLogged, 1, 0) == 0)
        {
            logger.WriteInformation(
                "City object preparation started after {ElapsedSeconds:F3}s: {DisplayName} ({PackageName}/{SlotKey}) mesh='{ActualMeshCode}'.",
                state.Runtime.ElapsedTotalSeconds,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.SlotKey,
                cityObject.ActualMeshCode);
        }

        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            state.Runtime.ProcessingCancellationToken);
        return await PrepareCityObjectAsync(
            state,
            routedClient,
            cityObject,
            diagnostics,
            logger,
            linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        ILogger logger,
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
            logger,
            cancellationToken);
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask.WaitAsync(cancellationToken);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay = preparedTextures
            .OfType<PreparedTerrainOverlayTextureReference>()
            .ToDictionary(
                static texture => texture.Overlay,
                static texture => texture.GeneratedTerrainTexture);
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
            logger.WriteInformation(
                "First city object prepared in {PrepareElapsedSeconds:F3}s after scene start {ElapsedSeconds:F3}s: {DisplayName} (textures={TextureCount}, geometry={GeometryDescription}).",
                stopwatch.Elapsed.TotalSeconds,
                state.Runtime.ElapsedTotalSeconds,
                cityObject.DisplayName,
                preparedTextures.Length,
                PreparedConstructionGeometryFormatter.Describe(preparedGeometry));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

}
