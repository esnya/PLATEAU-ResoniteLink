using System;
using System.Globalization;
using System.IO;
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

        (string TerrainMeshCode, TerrainTextureOverlay TerrainOverlay)[] distinctTerrainOverlays = cityObject.Materials
            .Select((material, materialIndex) => (Material: material, MaterialIndex: materialIndex))
            .Where(static entry => entry.Material.TerrainOverlay is not null && entry.Material.TerrainMeshCode is not null)
            .Select(entry => (
                TerrainMeshCode: ResoniteTerrainOverlayMaterialContract.ValidateMeshCode(
                    cityObject,
                    entry.MaterialIndex,
                    entry.Material,
                    entry.Material.TerrainMeshCode!,
                    entry.Material.TerrainOverlay!),
                TerrainOverlay: entry.Material.TerrainOverlay!))
            .Distinct()
            .OrderBy(static entry => entry.TerrainMeshCode, StringComparer.Ordinal)
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
        string terrainMeshCode,
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        GeneratedTerrainTexture terrainTexture = await terrainTextureAssetGenerator.EnsureTextureAsync(
            terrainTextureOverlay,
            cancellationToken);
        TerrainTextureSource[] usedSources = GetTrackedTerrainTextureSources(terrainTexture, terrainTextureOverlay);
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

        return new PreparedTextureReference(
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            TextureImport: terrainTexture.TextureImport,
            TerrainMeshCode: terrainMeshCode,
            TerrainOverlay: terrainTextureOverlay,
            GeneratedTerrainTexture: terrainTexture);
    }

    private static TerrainTextureSource[] GetTrackedTerrainTextureSources(
        GeneratedTerrainTexture terrainTexture,
        TerrainTextureOverlay terrainTextureOverlay)
    {
        if (terrainTexture.UsedSources is { Count: > 0 })
        {
            return terrainTexture.UsedSources
                .Distinct()
                .ToArray();
        }

        return
        [
            terrainTexture.UsedSource ?? terrainTextureOverlay.PrimarySource,
        ];
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
                $"GeoTIFF(path='{Path.GetFileName(rasterSource.SourcePath)}', crs='{rasterSource.Metadata?.CoordinateSystemIdentifier ?? "unknown"}')"),
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
            new PreparedTextureReference(
                TexturePayload: material.TexturePayload,
                TextureSourceKind: material.TextureSourceKind,
                TextureImport: ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
                TerrainMeshCode: null,
                TerrainOverlay: null));
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
