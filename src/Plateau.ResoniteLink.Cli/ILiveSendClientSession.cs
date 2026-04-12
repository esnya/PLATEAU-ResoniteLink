using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal interface ILiveSendClientSession
{
    IResoniteLinkClient? SetupClient { get; }

    void BeginWorkerClientTracking();

    Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken);

    Task<IResoniteLinkClient> CreateWorkerClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken);

    Task<IResoniteLinkClient> CreateLaneClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken);

    void DisposeClients();
}
