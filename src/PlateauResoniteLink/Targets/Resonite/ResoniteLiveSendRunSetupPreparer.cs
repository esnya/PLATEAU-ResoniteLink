using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendPreparedRunSetup(
    LiveSendRunPlan RunPlan,
    ResoniteSceneSetupState SetupState,
    LiveSendProgressSink Progress,
    CommonMaterialAssetCache Materials,
    ResoniteSharedSlotIndex Placement);

internal sealed class ResoniteLiveSendRunSetupPreparer(
    IResoniteSceneSetupInterpreter sceneSetupInterpreter,
    IResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer,
    ResonitePreparedRunSetupComposer preparedRunSetupComposer)
{
    public async Task<LiveSendPreparedRunSetup> PrepareAsync(
        LiveSendRunPlan runPlan,
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runPlan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

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
        LiveSendPreparedRunSetup preparedSetup = preparedRunSetupComposer.Compose(
            runPlan,
            setupState);
        ReportSetupCommonMaterials(setupState, context);
        await commonMaterialSetupPreparer.PrepareAsync(
            GetRoutedClient(context),
            setupState,
            preparedSetup.Materials,
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
        return preparedSetup;
    }

    private static void ReportSetupCommonMaterials(
        ResoniteSceneSetupState setupState,
        LiveSendRunStartContext context)
    {
        if (setupState.CommonMaterialAssets.Count > 0)
        {
            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"Setup batch prepared {setupState.CommonMaterialAssets.Count} textureless common materials."));
            return;
        }

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                "Setup created common material slots; no textureless common material components were needed in setup batch."));
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
