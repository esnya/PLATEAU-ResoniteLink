using System;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendResourceReleaser
{
    ValueTask ReleaseAsync(ResoniteLiveSendResourceRelease release);
}

internal sealed class ResoniteLiveSendResourceReleaser : IResoniteLiveSendResourceReleaser
{
    public async ValueTask ReleaseAsync(ResoniteLiveSendResourceRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(release.ClientSession);

        if (release.State is not null)
        {
            await release.State.Runtime.DisposeAsync();
        }

        switch (release.ClientRelease)
        {
            case ResoniteLiveSendClientRelease.None:
                break;
            case ResoniteLiveSendClientRelease.Reset:
                await release.ClientSession.ResetClientsAsync();
                break;
            case ResoniteLiveSendClientRelease.Dispose:
                release.ClientSession.DisposeClients();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(release),
                    release.ClientRelease,
                    "Unknown live-send client release action.");
        }
    }
}

internal sealed record ResoniteLiveSendResourceRelease(
    LiveSendRunState? State,
    ILiveSendClientSession ClientSession,
    ResoniteLiveSendClientRelease ClientRelease);

internal enum ResoniteLiveSendClientRelease
{
    None,
    Reset,
    Dispose,
}
