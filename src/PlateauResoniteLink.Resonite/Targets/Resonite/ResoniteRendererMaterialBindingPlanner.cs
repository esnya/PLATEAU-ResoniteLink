using System;
using System.Collections.Generic;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteRendererMaterialBindingPlanner
{
    internal static PlannedMainTextureOverrideRendererMaterialBinding CreateMainTextureOverrideRendererBinding(
        PlannedMaterialAsset materialAsset,
        LocalRendererOverrideTextureProvider mainTexture,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
        ResoniteMaterialBinding sourceMaterial)
    {
        ArgumentNullException.ThrowIfNull(materialAsset);
        ArgumentNullException.ThrowIfNull(mainTexture);
        ArgumentNullException.ThrowIfNull(terrainTexturePropertyBlockComponentsByMeshCode);
        ArgumentNullException.ThrowIfNull(sourceMaterial);

        if (sourceMaterial.TerrainOverlay is null)
        {
            return new PlannedAlbedoMainTextureOverrideRendererMaterialBinding(materialAsset, mainTexture);
        }

        ResoniteComponentLocator? sharedMainTexturePropertyBlockComponent =
            sourceMaterial.TerrainOverlayMaterial is not null
            && terrainTexturePropertyBlockComponentsByMeshCode.TryGetValue(sourceMaterial.TerrainOverlayMaterial.MeshCode, out ResoniteComponentLocator propertyBlockComponent)
                ? propertyBlockComponent
                : null;
        return new PlannedTerrainMainTextureOverrideRendererMaterialBinding(
            materialAsset,
            new SharedTerrainOverlayTextureProvider(
                mainTexture.AssetUri,
                SharedMainTextureComponent: null,
                sharedMainTexturePropertyBlockComponent));
    }
}
