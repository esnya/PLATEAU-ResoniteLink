using System;
using System.Runtime.ExceptionServices;
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

        ExceptionDispatchInfo? runtimeDisposeFailure = null;
        try
        {
            if (release.State is not null)
            {
                await release.State.Runtime.DisposeAsync();
            }
        }
#pragma warning disable CA1031
        catch (Exception exception)
        {
            runtimeDisposeFailure = ExceptionDispatchInfo.Capture(exception);
        }
#pragma warning restore CA1031

        try
        {
            await ReleaseClientAsync(release);
        }
#pragma warning disable CA1031
        catch when (runtimeDisposeFailure is not null)
        {
            runtimeDisposeFailure.Throw();
        }
#pragma warning restore CA1031

        runtimeDisposeFailure?.Throw();
    }

    private static async ValueTask ReleaseClientAsync(ResoniteLiveSendResourceRelease release)
    {
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
                throw new ArgumentException(
                    $"Unknown {nameof(ResoniteLiveSendResourceRelease.ClientRelease)} value '{release.ClientRelease}'.",
                    nameof(release));
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
