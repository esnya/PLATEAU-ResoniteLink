using System.Net.Http;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

public interface IResoniteLiveSceneImportFactory
{
    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal interface IResoniteRecordingLiveSceneImportFactory
{
    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSceneImportFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    ResoniteLiveSceneImportDependencyFactory dependencyFactory,
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory) :
    IResoniteLiveSceneImportFactory
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled()
            : ResoniteLinkSendDiagnostics.Disabled;
        ILiveSendClientSession clientSession = clientSessionFactory.Create(options, diagnostics);
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator =
            terrainTextureAssetGeneratorFactory.Create(
                terrainTextureAssetHttpClient,
                new TerrainTextureAssetGeneratorOptions(
                    options.TerrainTileCacheRoot,
                    options.DisableTerrainTileCache));
        ResoniteLiveSceneImportDependencies dependencies = dependencyFactory.Create(
            options,
            clientSession,
            diagnostics,
            terrainTextureAssetGenerator);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }
}

internal sealed class ResoniteRecordingLiveSceneImportFactory(
    ResoniteLiveSceneImportDependencyFactory dependencyFactory) : IResoniteRecordingLiveSceneImportFactory
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ResoniteLiveSceneImportDependencies dependencies = dependencyFactory.Create(
            options,
            clientSession,
            diagnostics,
            terrainTextureAssetGenerator);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }
}
