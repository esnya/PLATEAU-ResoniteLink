using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

/// <summary>
/// Coordinates authoritative live-send mutation ordering.
/// This is the seam between setup-session ownership, shared-slot visibility checks,
/// and anchor-root placement.
/// </summary>
internal interface IAuthoritativeSceneMutationCoordinator
{
    Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken);

    Task<IResoniteLinkClient> CreateLaneClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken);

    Task<SceneAnchor> ResolveAnchorAsync(
        IResoniteLinkClient setupClient,
        string datasetRootSlotId,
        string completionMeshCode,
        bool datasetRootExisted,
        CancellationToken cancellationToken);
}
