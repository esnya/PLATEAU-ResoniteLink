using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Transport.ResoniteLink;

internal interface ILiveSendClientSession
{
    IResoniteLinkClient? RoutedClient { get; }

    ResoniteLinkSendDiagnostics Diagnostics { get; }

    Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken);

    ValueTask ResetClientsAsync(CancellationToken cancellationToken = default);

    void DisposeClients();
}
