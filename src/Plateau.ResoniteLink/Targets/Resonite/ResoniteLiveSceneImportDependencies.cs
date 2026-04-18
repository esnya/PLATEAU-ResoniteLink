namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator)
{
}
