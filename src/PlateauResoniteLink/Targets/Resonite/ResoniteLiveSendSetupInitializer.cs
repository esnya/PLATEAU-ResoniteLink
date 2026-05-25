using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendSetupInitialization(
    ResoniteSceneSetupState SetupState,
    LiveSendProgressSink Progress,
    CommonMaterialAssetCache Materials,
    ResoniteSharedSlotIndex Placement);

internal interface IResoniteLiveSendSetupInitializer
{
    Task<LiveSendSetupInitialization> InitializeAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        LiveSendRunPlan runPlan,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendSetupInitializer(
    IResoniteSceneSetupInterpreter sceneSetupInterpreter,
    IResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer,
    IResoniteCommonMaterialSetupCachePrimer commonMaterialSetupCachePrimer,
    IResoniteSharedSlotIndexFactory sharedSlotIndexFactory) : IResoniteLiveSendSetupInitializer
{
    public async Task<LiveSendSetupInitialization> InitializeAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        LiveSendRunPlan runPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CommonMaterials);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.ClientSession);
        ArgumentNullException.ThrowIfNull(runPlan);

        LiveSendProgressSink progress = new();
        CommonMaterialAssetCache materials = new();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                "Reusing dataset content source provided by caller."));
        ReportProgress(
            context,
            PlateauLog.Info("live", "Setting up mutable helpers (baker)."));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                "Starting setup slot setup: dataset root, assets root, common assets root, location slot, and source-file root reference."));
        Stopwatch setupStopwatch = Stopwatch.StartNew();
        ResoniteSceneSetupState setupState = await sceneSetupInterpreter.SetupAsync(
            GetRoutedClient(context),
            runPlan.SetupInfo,
            request.CommonMaterials,
            cancellationToken);
        setupStopwatch.Stop();
        ResoniteSharedSlotIndex placement = sharedSlotIndexFactory.Create(setupState, runPlan);
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Scene setup complete in {setupStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset_root={setupState.DatasetRootSlot.SlotName}, assets_root={setupState.DatasetAssetsRootSlot.SlotName}, "
                + $"common_root={setupState.CommonAssetsRootSlot.SlotName}, "
                + $"dataset_root_existed={setupState.DatasetRootExisted}, "
                + $"location_slot='{setupState.SceneAnchor.LocationSlot.Value}', "
                + $"anchor_mesh='{setupState.SceneAnchor.MeshCode}', "
                + $"anchor_source_file_root='{setupState.SceneAnchor.ReferenceSourceFileRoot?.Value ?? "<pending>"}')."));
        commonMaterialSetupCachePrimer.Prime(
            setupState,
            materials,
            progress,
            context.ProgressReporter);

        await commonMaterialSetupPreparer.PrepareAsync(
            GetRoutedClient(context),
            setupState,
            materials,
            request.CommonMaterials,
            context.ProgressReporter,
            cancellationToken);

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                "setup fixed dataset license metadata/component before city-object streaming starts."));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Dataset metadata/license phase complete during setup. "
                + $"Dataset root existed={setupState.DatasetRootExisted}."));

        return new LiveSendSetupInitialization(
            setupState,
            progress,
            materials,
            placement);
    }

    private static IResoniteLinkClient GetRoutedClient(LiveSendRunStartContext context)
    {
        return context.ClientSession.GetRequiredClient();
    }

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
