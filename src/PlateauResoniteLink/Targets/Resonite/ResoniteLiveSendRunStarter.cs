using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunStartRequest(
    ResoniteSceneSetupInfo SetupInfo,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials,
    PlateauImportRequest NormalizedRequest,
    ResoniteLocalOrigin RequestLocalOrigin,
    ResoniteImportMemoryProfile MemoryProfile,
    int ConnectionCount,
    bool MeshBakeEnabled);

internal sealed record LiveSendRunStartContext(
    Uri Endpoint,
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);

internal interface IResoniteLiveSendRunStarter
{
    Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunStarter(
    IResoniteSceneSetupInterpreter sceneSetupInterpreter,
    IResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer,
    ILiveSendRunPlanFactory runPlanFactory,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendWorkerLauncher workerLauncher,
    IResoniteSlotCreator slotCreator) : IResoniteLiveSendRunStarter
{
    public async Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
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
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {context.Endpoint} "
                + $"with {request.ConnectionCount} available routed connection(s)."));
        await context.ClientSession.EnsureConnectedAsync(
            new LiveSendConnectionRequest(
                request.NormalizedRequest.Dataset,
                request.NormalizedRequest.MeshCode),
            cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{request.SetupInfo.Dataset}', mesh='{request.SetupInfo.MeshCode}')."));
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
        ResoniteSharedSlotIndex placement = new(
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            runPlan.RequestLocalOrigin,
            runPlan.SourceFileSlotNamesByRelativePath,
            setupState.SceneAnchor,
            slotCreator.CreateAsync);
        placement.IndexSetupHierarchy(setupState);
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
        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialAsset> materialAsset in setupState.CommonMaterialAssets.EnumerateMembers())
        {
            materials.CommonMaterialAssets.Set(materialAsset.Item);
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        if (setupState.CommonMaterialAssets.Count > 0)
        {
            progress.FirstCommonMaterialPrepLogged = setupState.CommonMaterialAssets.Count;
            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"Setup batch prepared {setupState.CommonMaterialAssets.Count} textureless common materials."));
        }
        else
        {
            ReportProgress(context, PlateauLog.Info("live", "Setup created common material slots; no textureless common material components were needed in setup batch."));
        }

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
        LiveSendRunState state = runStateFactory.Create(
            runPlan,
            setupState,
            progress,
            materials,
            placement,
            cancellationToken);
        workerLauncher.Launch(
            new LiveSendWorkerLaunchRequest(
                state,
                runPlan.Queue,
                runPlan.ResourceBudget,
                request.ConnectionCount),
            context);
        return state;
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
