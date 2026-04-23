using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ILiveSendClientSession
{
    ResoniteLinkSendDiagnostics Diagnostics { get; }

    IResoniteLinkClient GetRequiredClient();

    Task EnsureConnectedAsync(
        LiveSendConnectionRequest request,
        CancellationToken cancellationToken);

    ValueTask ResetClientsAsync(CancellationToken cancellationToken = default);

    void DisposeClients();
}
