using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteSceneBootstrapCoordinator
{
    Task<ResoniteSceneBootstrapState> BootstrapAsync(
        IResoniteLinkClient setupClient,
        ResoniteConstructionMetadata metadata,
        CancellationToken cancellationToken);
}
