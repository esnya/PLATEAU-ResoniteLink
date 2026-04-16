using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

/// <summary>
/// Coordinates authoritative live-send mutation operations.
/// </summary>
internal interface IAuthoritativeSceneMutationCoordinator
{
    Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken);

    Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        return EnsureConnectedAsync(request, cancellationToken);
    }

    Task<SceneAnchor> ResolveAnchorAsync(
        IResoniteLinkClient routedClient,
        string datasetRootSlotId,
        string completionMeshCode,
        bool datasetRootExisted,
        CancellationToken cancellationToken);
}

