using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Transport.ResoniteLink;

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
