namespace Plateau.ResoniteLink.Cli;

internal sealed record ResoniteLinkSceneBuilderDependencies(
    Func<IResoniteLinkClient> ClientFactory,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator)
{
}
