using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportFactory
{
    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);

    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSceneImportFactory(
    IResoniteLiveSceneImportDependencyFactory dependencyFactory) : IResoniteLiveSceneImportFactory
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ResoniteLiveSceneImportDependencies dependencies = dependencyFactory.Create(
            options,
            terrainTextureAssetHttpClient);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }

    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ResoniteLiveSceneImportDependencies dependencies = dependencyFactory.Create(
            options,
            clientSession,
            terrainTextureAssetGenerator);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }
}
