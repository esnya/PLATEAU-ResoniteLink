using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteQueuedTexturePreparer(
    ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
{
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator =
        terrainTextureAssetGenerator ?? throw new ArgumentNullException(nameof(terrainTextureAssetGenerator));

    public async Task<PreparedTextureReference[]> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(cityObject);

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
                routedClient, entry.TerrainMeshCode,
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
        ThirdRegionalMeshCode terrainMeshCode,
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        GeneratedTerrainTexture terrainTexture = await terrainTextureAssetGenerator.EnsureTextureAsync(
            terrainTextureOverlay,
            cancellationToken);
        TerrainTextureSourceUsage[] usages = GetTrackedTerrainTextureSourceUsages(terrainTexture);
        foreach (TerrainTextureSourceUsage usage in usages)
        {
            int useCount = state.DemSourceUseCounts.AddOrUpdate(
                usage,
                1,
                static (_, current) => checked(current + 1));
            if (useCount == 1)
            {
                PlateauDiagnostics.Progress(
                    "Resolved DEM terrain texture source for package '{PackageName}' to {TerrainTextureSource}.",
                    terrainTextureOverlay.PackageName,
                    usage.Description);
            }

            if (usage.RequiresGsiFallbackLicense)
            {
                await EnsureGsiFallbackLicenseAsync(state, routedClient, cancellationToken);
            }
        }

        return new PreparedTerrainOverlayTextureReference(
            terrainMeshCode,
            terrainTextureOverlay,
            terrainTexture);
    }

    private static TerrainTextureSourceUsage[] GetTrackedTerrainTextureSourceUsages(
        GeneratedTerrainTexture terrainTexture)
    {
        return terrainTexture.Usages
            .Distinct()
            .ToArray();
    }

    private static async Task EnsureGsiFallbackLicenseAsync(
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

            await ResoniteDatasetLicenseWriter.EnsureGsiFallbackLicenseAsync(
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

}
