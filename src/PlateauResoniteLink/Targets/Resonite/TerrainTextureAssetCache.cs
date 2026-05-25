using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class TerrainTextureAssetCache
{
    public AsyncInFlightResultCache<string, SharedTerrainTextureAsset> AssetsByMeshCode { get; } = new();
}

internal sealed record SharedTerrainTextureAsset(
    Uri TextureUri,
    CreatedComponent TextureComponent,
    CreatedComponent MainTexturePropertyBlockComponent);
