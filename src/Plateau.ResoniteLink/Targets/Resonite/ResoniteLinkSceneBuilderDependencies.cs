namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed record ResoniteLinkSceneBuilderDependencies(
    ILiveSendClientSession ClientSession,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator)
{
}
