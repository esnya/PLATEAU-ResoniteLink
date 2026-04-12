namespace Plateau.ResoniteLink.Cli;

internal sealed record ResoniteLinkSceneBuilderDependencies(
    Func<IResoniteLinkClient> ClientFactory,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator)
{
    public static ResoniteLinkSceneBuilderDependencies CreateDefault()
    {
        return new(
            static () => new ResoniteLinkClient(),
            new TerrainTextureAssetGenerator());
    }
}
