using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Diagnostics;

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

        context.Logger.WriteInformation(
            "Initializing scene state for dataset '{Dataset}' mesh '{MeshCode}' at '{ResolvedWorkRoot}'.",
            request.SetupInfo.Dataset,
            request.SetupInfo.MeshCode,
            runPlan.ResolvedWorkRoot);
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        context.Logger.WriteInformation(
            "Connecting ResoniteLink connection pool to {Endpoint} with {ConnectionCount} available routed connection(s).",
            context.Endpoint,
            request.ConnectionCount);
        await context.ClientSession.EnsureConnectedAsync(
            request.ConnectionRequest,
            cancellationToken);
        connectionStopwatch.Stop();
        context.Logger.WriteInformation(
            "ResoniteLink connection pool ready in {ElapsedSeconds:F2}s (dataset='{Dataset}', mesh='{MeshCode}').",
            connectionStopwatch.Elapsed.TotalSeconds,
            request.SetupInfo.Dataset,
            request.SetupInfo.MeshCode);
    }
}
