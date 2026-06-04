using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedTexturePreparer
{
    Task<PreparedTextureReference[]> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteQueuedTexturePreparer(
    ITerrainTextureAssetGenerator terrainTextureAssetGenerator,
    Execution.IResoniteDatasetLicenseWriter datasetLicenseWriter) : IResoniteQueuedTexturePreparer
{
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator =
        terrainTextureAssetGenerator ?? throw new ArgumentNullException(nameof(terrainTextureAssetGenerator));
    private readonly Execution.IResoniteDatasetLicenseWriter datasetLicenseWriter =
        datasetLicenseWriter ?? throw new ArgumentNullException(nameof(datasetLicenseWriter));

    public async Task<PreparedTextureReference[]> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(cityObject);

        (ThirdRegionalMeshCode TerrainMeshCode, TerrainTextureOverlay TerrainOverlay)[] distinctTerrainOverlayBindings = cityObject.Materials
            .Select((material, materialIndex) => (Material: material, MaterialIndex: materialIndex))
            .Where(static entry => entry.Material.TerrainOverlayMaterial is not null)
            .Select(entry => (
                TerrainMeshCode: entry.Material.TerrainOverlayMaterial!.MeshCode,
                TerrainOverlay: entry.Material.TerrainOverlayMaterial!.Overlay))
            .Distinct()
            .OrderBy(static entry => entry.TerrainMeshCode.Value, StringComparer.Ordinal)
            .ThenBy(static entry => entry.TerrainOverlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.TerrainOverlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.TerrainOverlay.GeographicBounds.MinLongitude)
            .ToArray();

        TerrainTextureOverlay[] distinctTerrainOverlays = distinctTerrainOverlayBindings
            .Select(static entry => entry.TerrainOverlay)
            .Distinct()
            .OrderBy(static overlay => overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static overlay => overlay.MeshCode.Value, StringComparer.Ordinal)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
            .ToArray();

        Task<(TerrainTextureOverlay Overlay, GeneratedTerrainTexture Texture)>[] terrainOverlayTexturePreparationTasks = distinctTerrainOverlays
            .Select(async overlay => (
                Overlay: overlay,
                Texture: await PrepareTerrainOverlayTextureAsync(
                state,
                routedClient,
                progressReporter,
                overlay,
                cancellationToken)))
            .ToArray();
        Task<PreparedTextureReference?>[] directTexturePreparationTasks = cityObject.Materials
            .Where(static material => material.TexturePayload is not null)
            .Select(PrepareDirectMaterialTextureReferenceAsync)
            .ToArray();
        await Task.WhenAll(terrainOverlayTexturePreparationTasks);
        PreparedTextureReference?[] preparedDirectTextureResults = await Task.WhenAll(directTexturePreparationTasks);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> terrainTexturesByOverlay = terrainOverlayTexturePreparationTasks
            .Select(static task => task.Result)
            .ToDictionary(static result => result.Overlay, static result => result.Texture);
        PreparedTerrainOverlayTextureReference[] preparedTerrainTextureReferences = distinctTerrainOverlayBindings
            .Select(entry => new PreparedTerrainOverlayTextureReference(
                entry.TerrainMeshCode,
                entry.TerrainOverlay,
                terrainTexturesByOverlay[entry.TerrainOverlay]))
            .ToArray();
        return preparedTerrainTextureReferences
            .Cast<PreparedTextureReference>()
            .Concat(preparedDirectTextureResults.OfType<PreparedTextureReference>())
            .OfType<PreparedTextureReference>()
            .ToArray();
    }

    private async Task<GeneratedTerrainTexture> PrepareTerrainOverlayTextureAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        Action<string>? progressReporter,
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        GeneratedTerrainTexture terrainTexture = await terrainTextureAssetGenerator.EnsureTextureAsync(
            terrainTextureOverlay,
            cancellationToken);
        TerrainTextureSource[] usedSources = GetTrackedTerrainTextureSources(terrainTexture);
        foreach (TerrainTextureSource usedSource in usedSources)
        {
            int useCount = state.DemSourceUseCounts.AddOrUpdate(
                usedSource.IdentityKey,
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

        return terrainTexture;
    }

    private static TerrainTextureSource[] GetTrackedTerrainTextureSources(
        GeneratedTerrainTexture terrainTexture)
    {
        return terrainTexture.UsedSources
            .Distinct()
            .ToArray();
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

            await datasetLicenseWriter.EnsureGsiFallbackLicenseAsync(
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
                TextureSource: ResoniteTextureImportFactory.CreateSourceFromPayload(material.TexturePayload)));
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
