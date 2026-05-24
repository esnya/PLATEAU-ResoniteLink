using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendConnectionInitializer
{
    Task EnsureConnectedAsync(
        ILiveSendClientSession clientSession,
        Uri endpoint,
        int connectionCount,
        ResoniteSceneSetupInfo setupInfo,
        PlateauImportRequest normalizedRequest,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendConnectionInitializer : IResoniteLiveSendConnectionInitializer
{
    public async Task EnsureConnectedAsync(
        ILiveSendClientSession clientSession,
        Uri endpoint,
        int connectionCount,
        ResoniteSceneSetupInfo setupInfo,
        PlateauImportRequest normalizedRequest,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(setupInfo);
        ArgumentNullException.ThrowIfNull(normalizedRequest);

        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {endpoint} "
                + $"with {connectionCount} available routed connection(s)."));
        await clientSession.EnsureConnectedAsync(
            new LiveSendConnectionRequest(
                normalizedRequest.Dataset,
                normalizedRequest.MeshCode),
            cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{setupInfo.Dataset}', mesh='{setupInfo.MeshCode}')."));
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
