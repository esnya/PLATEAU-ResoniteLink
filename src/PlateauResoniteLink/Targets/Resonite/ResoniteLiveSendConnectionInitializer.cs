using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendConnectionInitializer
{
    Task EnsureConnectedAsync(
        LiveSendRunStartRequest request,
        LiveSendRunPlan runPlan,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendConnectionInitializer : IResoniteLiveSendConnectionInitializer
{
    public async Task EnsureConnectedAsync(
        LiveSendRunStartRequest request,
        LiveSendRunPlan runPlan,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(runPlan);
        ArgumentNullException.ThrowIfNull(context);

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{request.SetupInfo.Dataset}' "
                + $"mesh '{request.SetupInfo.MeshCode}' at '{runPlan.ResolvedWorkRoot}'."));
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {context.Endpoint} "
                + $"with {request.ConnectionCount} available routed connection(s)."));
        await context.ClientSession.EnsureConnectedAsync(
            request.ConnectionRequest,
            cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{request.SetupInfo.Dataset}', mesh='{request.SetupInfo.MeshCode}')."));
    }

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
