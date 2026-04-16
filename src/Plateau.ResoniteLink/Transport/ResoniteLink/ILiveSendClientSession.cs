using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal interface ILiveSendClientSession
{
    IResoniteLinkClient? RoutedClient { get; }

    void BeginWorkerClientTracking();

    Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken);

    Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        return EnsureConnectedAsync(request, cancellationToken);
    }

    void DisposeClients();
}

