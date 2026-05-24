using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendSceneSetupRunner
{
    Task<ResoniteLiveSendSceneSetupResult> SetupAsync(
        IResoniteLinkClient routedClient,
        LiveSendRunPlan runPlan,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendSceneSetupRunner(
    IResoniteSceneSetupInterpreter sceneSetupInterpreter,
    IResoniteSharedSlotIndexFactory sharedSlotIndexFactory,
    IResoniteSlotCreator slotCreator) : IResoniteLiveSendSceneSetupRunner
{
    private readonly IResoniteSceneSetupInterpreter sceneSetupInterpreter =
        sceneSetupInterpreter ?? throw new ArgumentNullException(nameof(sceneSetupInterpreter));
    private readonly IResoniteSharedSlotIndexFactory sharedSlotIndexFactory =
        sharedSlotIndexFactory ?? throw new ArgumentNullException(nameof(sharedSlotIndexFactory));
    private readonly IResoniteSlotCreator slotCreator =
        slotCreator ?? throw new ArgumentNullException(nameof(slotCreator));

    public async Task<ResoniteLiveSendSceneSetupResult> SetupAsync(
        IResoniteLinkClient routedClient,
        LiveSendRunPlan runPlan,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(runPlan);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                "Starting setup slot setup: dataset root, assets root, common assets root, location slot, and source-file root reference."));
        Stopwatch setupStopwatch = Stopwatch.StartNew();
        ResoniteSceneSetupState setupState = await sceneSetupInterpreter.SetupAsync(
            routedClient,
            runPlan.SetupInfo,
            commonMaterials,
            cancellationToken);
        setupStopwatch.Stop();
        ResoniteSharedSlotIndex placement = sharedSlotIndexFactory.Create(
            setupState,
            runPlan.RequestLocalOrigin,
            runPlan.SourceFileSlotNamesByRelativePath,
            slotCreator.CreateAsync);
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Scene setup complete in {setupStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset_root={setupState.DatasetRootSlot.SlotName}, assets_root={setupState.DatasetAssetsRootSlot.SlotName}, "
                + $"common_root={setupState.CommonAssetsRootSlot.SlotName}, "
                + $"dataset_root_existed={setupState.DatasetRootExisted}, "
                + $"location_slot='{setupState.SceneAnchor.LocationSlot.Value}', "
                + $"anchor_mesh='{setupState.SceneAnchor.MeshCode}', "
                + $"anchor_source_file_root='{setupState.SceneAnchor.ReferenceSourceFileRoot?.Value ?? "<pending>"}')."));

        return new ResoniteLiveSendSceneSetupResult(setupState, placement);
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}

internal sealed record ResoniteLiveSendSceneSetupResult(
    ResoniteSceneSetupState SetupState,
    ResoniteSharedSlotIndex Placement);
