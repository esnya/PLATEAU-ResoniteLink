using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Diagnostics;

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
    ResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer,
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

        context.Logger.WriteInformation("Reusing dataset content source provided by caller.");
        context.Logger.WriteInformation("Setting up mutable helpers (baker).");
        context.Logger.WriteInformation("Starting setup slot setup: dataset root, assets root, common assets root, location slot, and source-file root reference.");
        Stopwatch setupStopwatch = Stopwatch.StartNew();
        ResoniteSceneSetupState setupState = await sceneSetupInterpreter.SetupAsync(
            GetRoutedClient(context),
            runPlan.SetupInfo,
            request.CommonMaterials,
            cancellationToken);
        setupStopwatch.Stop();
        context.Logger.WriteInformation(
            "Scene setup complete in {ElapsedSeconds:F2}s (dataset_root={DatasetRoot}, assets_root={AssetsRoot}, common_root={CommonRoot}, dataset_root_existed={DatasetRootExisted}, location_slot='{LocationSlot}', anchor_mesh='{AnchorMesh}', anchor_source_file_root='{AnchorSourceFileRoot}').",
            setupStopwatch.Elapsed.TotalSeconds,
            setupState.DatasetRootSlot.SlotName,
            setupState.DatasetAssetsRootSlot.SlotName,
            setupState.CommonAssetsRootSlot.SlotName,
            setupState.DatasetRootExisted,
            setupState.SceneAnchor.LocationSlot.Value,
            setupState.SceneAnchor.MeshCode,
            setupState.SceneAnchor.ReferenceSourceFileRoot?.Value ?? "<pending>");
        LiveSendPreparedRunSetup preparedSetup = preparedRunSetupComposer.Compose(
            runPlan,
            setupState);
        ReportSetupCommonMaterials(setupState, context);
        await commonMaterialSetupPreparer.PrepareAsync(
            GetRoutedClient(context),
            setupState,
            preparedSetup.Materials,
            request.CommonMaterials,
            context.Logger,
            cancellationToken);
        context.Logger.WriteInformation("setup fixed dataset license metadata/component before city-object streaming starts.");
        context.Logger.WriteInformation(
            "Dataset metadata/license phase complete during setup. Dataset root existed={DatasetRootExisted}.",
            setupState.DatasetRootExisted);
        return preparedSetup;
    }

    private static void ReportSetupCommonMaterials(
        ResoniteSceneSetupState setupState,
        LiveSendRunStartContext context)
    {
        if (setupState.CommonMaterialAssets.Count > 0)
        {
            context.Logger.WriteInformation(
                "Setup batch prepared {TexturelessCommonMaterialCount} textureless common materials.",
                setupState.CommonMaterialAssets.Count);
            return;
        }

        context.Logger.WriteInformation("Setup created common material slots; no textureless common material components were needed in setup batch.");
    }

    private static IResoniteLinkClient GetRoutedClient(LiveSendRunStartContext context)
    {
        return context.ClientSession.GetRequiredClient();
    }

}
