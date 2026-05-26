using System;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunResourceReleaser
{
    ValueTask ReleaseAsync(
        LiveSendRunState? state,
        ILiveSendClientSession clientSession,
        bool disposeClients,
        bool resetClients);
}

internal sealed class ResoniteLiveSendRunResourceReleaser : IResoniteLiveSendRunResourceReleaser
{
    public async ValueTask ReleaseAsync(
        LiveSendRunState? state,
        ILiveSendClientSession clientSession,
        bool disposeClients,
        bool resetClients)
    {
        ArgumentNullException.ThrowIfNull(clientSession);

        if (state is not null)
        {
            try
            {
                await state.Runtime.DisposeAsync();
            }
            finally
            {
                state.GsiFallbackLicenseGate.Dispose();
            }
        }

        if (disposeClients)
        {
            clientSession.DisposeClients();
        }
        else if (resetClients)
        {
            await clientSession.ResetClientsAsync();
        }
    }
}
