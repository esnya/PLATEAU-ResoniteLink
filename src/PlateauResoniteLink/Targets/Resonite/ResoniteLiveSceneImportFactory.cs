using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportFactory
{
    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal sealed class ResoniteLiveSceneImportFactory(
    IResoniteLiveSceneImportDependencyFactory dependencyFactory) : IResoniteLiveSceneImportFactory
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        return new ResoniteLiveSceneImportTarget(
            options,
            dependencyFactory.CreateSession(options),
            dependencyFactory.CreateExecutionServices(options, terrainTextureAssetHttpClient));
    }
}
