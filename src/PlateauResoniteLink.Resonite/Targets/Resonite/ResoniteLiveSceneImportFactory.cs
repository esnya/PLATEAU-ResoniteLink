using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Targets.Resonite;

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
    ResoniteLiveSceneImportDependencyFactory dependencyFactory,
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory) :
    IResoniteLiveSceneImportFactory
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ResoniteLiveSceneImportDependencies dependencies = dependencyFactory.Create(
            options,
            terrainTextureAssetHttpClient,
            terrainTextureAssetGeneratorFactory);
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
