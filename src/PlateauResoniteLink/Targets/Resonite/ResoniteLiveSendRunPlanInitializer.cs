using System;

using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunPlanInitializer
{
    LiveSendRunPlan Initialize(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context);
}

internal sealed class ResoniteLiveSendRunPlanInitializer(
    ILiveSendRunPlanFactory runPlanFactory) : IResoniteLiveSendRunPlanInitializer
{
    public LiveSendRunPlan Initialize(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request.SetupInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkRoot);
        ArgumentNullException.ThrowIfNull(request.CommonMaterials);
        ArgumentNullException.ThrowIfNull(request.NormalizedRequest);
        ArgumentNullException.ThrowIfNull(context.Endpoint);
        ArgumentNullException.ThrowIfNull(context.ClientSession);
        ArgumentNullException.ThrowIfNull(context.Diagnostics);

        LiveSendRunPlan runPlan = runPlanFactory.Create(
            request.SetupInfo,
            request.WorkRoot,
            request.RequestLocalOrigin,
            request.MemoryProfile,
            request.ConnectionCount,
            request.MeshBakeEnabled);
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{request.SetupInfo.Dataset}' "
                + $"mesh '{request.SetupInfo.MeshCode}' at '{runPlan.ResolvedWorkRoot}'."));
        return runPlan;
    }

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
