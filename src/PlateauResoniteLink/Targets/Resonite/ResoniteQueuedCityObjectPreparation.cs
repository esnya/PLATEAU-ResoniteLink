using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteQueuedCityObjectPreparation(
    GenerateTerrainTexture generateTerrainTexture,
    EnsureResoniteGsiFallbackLicense ensureGsiFallbackLicense)
{
    private readonly GenerateTerrainTexture generateTerrainTexture =
        generateTerrainTexture ?? throw new ArgumentNullException(nameof(generateTerrainTexture));
    private readonly EnsureResoniteGsiFallbackLicense ensureGsiFallbackLicense =
        ensureGsiFallbackLicense ?? throw new ArgumentNullException(nameof(ensureGsiFallbackLicense));

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
        PreparedTextureReference[] preparedTextures = await PrepareTexturesAsync(
            state,
            routedClient,
            cityObject,
            progressReporter,
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

    private async Task<PreparedTextureReference[]> PrepareTexturesAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        (ThirdRegionalMeshCode TerrainMeshCode, TerrainTextureOverlay TerrainOverlay)[] distinctTerrainOverlays = cityObject.Materials
            .Select((material, materialIndex) => (Material: material, MaterialIndex: materialIndex))
            .Where(static entry => entry.Material.TerrainOverlayMaterial is not null)
            .Select(entry => (
                TerrainMeshCode: ResoniteTerrainOverlayMaterialContract.ValidateMeshCode(
                    cityObject,
                    entry.MaterialIndex,
                    entry.Material,
                    entry.Material.TerrainOverlayMaterial!.MeshCode,
                    entry.Material.TerrainOverlayMaterial.Overlay),
                TerrainOverlay: entry.Material.TerrainOverlayMaterial!.Overlay))
            .Distinct()
            .OrderBy(static entry => entry.TerrainMeshCode.Value, StringComparer.Ordinal)
            .ThenBy(static entry => entry.TerrainOverlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.TerrainOverlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.TerrainOverlay.GeographicBounds.MinLongitude)
            .ToArray();

        Task<PreparedTextureReference?>[] terrainOverlayTexturePreparationTasks = distinctTerrainOverlays
            .Select(entry => PrepareTerrainOverlayTextureReferenceAsync(
                state,
                routedClient,
                progressReporter,
                entry.TerrainMeshCode,
                entry.TerrainOverlay,
                cancellationToken))
            .ToArray();
        PreparedTextureReference?[] preparedTextureResults = await Task.WhenAll(
            terrainOverlayTexturePreparationTasks
                .Concat(cityObject.Materials
                    .Where(static material => material.TexturePayload is not null)
                    .Select(PrepareDirectMaterialTextureReferenceAsync)
                    .ToArray()));
        return preparedTextureResults
            .OfType<PreparedTextureReference>()
            .ToArray();
    }

    private async Task<PreparedTextureReference?> PrepareTerrainOverlayTextureReferenceAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        Action<string>? progressReporter,
        ThirdRegionalMeshCode terrainMeshCode,
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        GeneratedTerrainTexture terrainTexture = await generateTerrainTexture(
            terrainTextureOverlay,
            cancellationToken);
        TerrainTextureSource[] usedSources = terrainTexture.UsedSources
            .Distinct()
            .ToArray();
        foreach (TerrainTextureSource usedSource in usedSources)
        {
            int useCount = state.DemSourceUseCounts.AddOrUpdate(
                usedSource,
                1,
                static (_, current) => checked(current + 1));
            if (useCount == 1)
            {
                ReportProgress(
                    progressReporter,
                    PlateauLog.Info(
                        "live",
                        $"Resolved DEM terrain texture source for package '{terrainTextureOverlay.PackageName}' "
                        + $"to {DescribeTerrainTextureSource(usedSource)}."));
            }

            if (IsGsiFallbackSource(usedSource))
            {
                await EnsureGsiFallbackLicenseAsync(state, routedClient, cancellationToken);
            }
        }

        return new PreparedTerrainOverlayTextureReference(
            terrainMeshCode,
            terrainTextureOverlay,
            terrainTexture);
    }

    private async Task EnsureGsiFallbackLicenseAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref state.GsiFallbackLicenseEnsured) != 0)
        {
            return;
        }

        await state.GsiFallbackLicenseGate.WaitAsync(cancellationToken);
        try
        {
            if (state.GsiFallbackLicenseEnsured != 0)
            {
                return;
            }

            await ensureGsiFallbackLicense(
                routedClient,
                state.Context.DatasetRootSlot,
                cancellationToken);
            Volatile.Write(ref state.GsiFallbackLicenseEnsured, 1);
        }
        finally
        {
            state.GsiFallbackLicenseGate.Release();
        }
    }

    private static bool IsGsiFallbackSource(TerrainTextureSource source)
    {
        return DemTerrainTextureDefaults.IsGsiFallbackSource(source);
    }

    private static string DescribeTerrainTextureSource(TerrainTextureSource source)
    {
        return source switch
        {
            TerrainTextureGeoReferencedRasterSource rasterSource => string.Create(
                CultureInfo.InvariantCulture,
                $"Geo-referenced raster(source='{rasterSource.ContentSource.Description}', crs='{rasterSource.Metadata.CoordinateSystemIdentifier}')"),
            TerrainTextureTileSource tileSource when IsGsiFallbackSource(tileSource) => string.Create(
                CultureInfo.InvariantCulture,
                $"GSI seamless photo tile(z={tileSource.ZoomLevel})"),
            TerrainTextureTileSource tileSource => string.Create(
                CultureInfo.InvariantCulture,
                $"PLATEAU-Ortho tile(z={tileSource.ZoomLevel})"),
            _ => source.GetType().Name,
        };
    }

    private static Task<PreparedTextureReference?> PrepareDirectMaterialTextureReferenceAsync(
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(material.TexturePayload);

        return Task.FromResult<PreparedTextureReference?>(
            new PreparedMaterialTextureReference(
                TexturePayload: material.TexturePayload,
                TextureSourceKind: material.TextureSourceKind,
                TextureSource: material.TexturePayload.Source));
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
