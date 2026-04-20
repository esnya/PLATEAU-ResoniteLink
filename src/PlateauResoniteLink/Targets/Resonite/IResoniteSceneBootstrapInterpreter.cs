using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneBootstrapInterpreter
{
    Task<ResoniteSceneBootstrapState> BootstrapAsync(
        IResoniteLinkClient setupClient,
        SceneBootstrapInfo setupInfo,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken);
}
