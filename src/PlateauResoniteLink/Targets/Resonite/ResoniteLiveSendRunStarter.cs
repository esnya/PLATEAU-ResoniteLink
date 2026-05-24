using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarter
{
    Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        CancellationToken cancellationToken);
}

internal sealed record LiveSendRunStartRequest(
    ResoniteSceneSetupInfo SetupInfo,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials,
    PlateauImportRequest NormalizedRequest,
    ResoniteLocalOrigin RequestLocalOrigin,
    ILiveSendClientSession ClientSession,
    Uri Endpoint,
    int ConnectionCount,
    ResoniteImportMemoryProfile MemoryProfile,
    bool MeshBakeEnabled,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter,
    ResoniteQueuedCityObjectProcessor ProcessQueuedCityObjectAsync);

internal sealed class ResoniteLiveSendRunStarter(
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendSceneSetupRunner sceneSetupRunner,
    IResoniteLiveSendWorkerLauncher workerLauncher,
    ILiveSendRunPlanFactory runPlanFactory,
    IResoniteCommonMaterialSetupAssetPreparer commonMaterialSetupAssetPreparer,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteTextureImageLoaderFactory textureImageLoaderFactory) : IResoniteLiveSendRunStarter
{
    private readonly IResoniteLiveSendConnectionInitializer connectionInitializer =
        connectionInitializer ?? throw new ArgumentNullException(nameof(connectionInitializer));
    private readonly IResoniteLiveSendSceneSetupRunner sceneSetupRunner =
        sceneSetupRunner ?? throw new ArgumentNullException(nameof(sceneSetupRunner));
    private readonly IResoniteLiveSendWorkerLauncher workerLauncher =
        workerLauncher ?? throw new ArgumentNullException(nameof(workerLauncher));
    private readonly ILiveSendRunPlanFactory runPlanFactory =
        runPlanFactory ?? throw new ArgumentNullException(nameof(runPlanFactory));
    private readonly IResoniteCommonMaterialSetupAssetPreparer commonMaterialSetupAssetPreparer =
        commonMaterialSetupAssetPreparer ?? throw new ArgumentNullException(nameof(commonMaterialSetupAssetPreparer));
    private readonly ILiveSendRunStateFactory runStateFactory =
        runStateFactory ?? throw new ArgumentNullException(nameof(runStateFactory));
    private readonly IResoniteTextureImageLoaderFactory textureImageLoaderFactory =
        textureImageLoaderFactory ?? throw new ArgumentNullException(nameof(textureImageLoaderFactory));

    public async Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SetupInfo);
        ArgumentNullException.ThrowIfNull(request.CommonMaterials);
        ArgumentNullException.ThrowIfNull(request.NormalizedRequest);
        ArgumentNullException.ThrowIfNull(request.ClientSession);
        ArgumentNullException.ThrowIfNull(request.Endpoint);
        ArgumentNullException.ThrowIfNull(request.Diagnostics);
        ArgumentNullException.ThrowIfNull(request.ProcessQueuedCityObjectAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkRoot);

        string resolvedWorkRoot = Path.GetFullPath(request.WorkRoot);
        LiveSendRunPlan runPlan = runPlanFactory.Create(
            request.SetupInfo,
            resolvedWorkRoot,
            request.RequestLocalOrigin,
            request.MemoryProfile,
            request.ConnectionCount,
            request.MeshBakeEnabled);
        ReportProgress(
            request.ProgressReporter,
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{request.SetupInfo.Dataset}' "
                + $"mesh '{request.SetupInfo.MeshCode}' at '{resolvedWorkRoot}'."));
        await connectionInitializer.EnsureConnectedAsync(
            request.ClientSession,
            request.Endpoint,
            request.ConnectionCount,
            request.SetupInfo,
            request.NormalizedRequest,
            request.ProgressReporter,
            cancellationToken);
        IResoniteLinkClient routedClient = request.ClientSession.GetRequiredClient();
        LiveSendProgressSink progress = new();
        CommonMaterialAssetCache materials = new();
        ReportProgress(
            request.ProgressReporter,
            PlateauLog.Info(
                "live",
                "Reusing dataset content source provided by caller."));
        ResoniteTextureImageLoader textureImageLoader = textureImageLoaderFactory.Create();
        ReportProgress(
            request.ProgressReporter,
            PlateauLog.Info("live", "Setting up mutable helpers (baker)."));
        ResoniteLiveSendSceneSetupResult sceneSetup = await sceneSetupRunner.SetupAsync(
            routedClient,
            runPlan,
            request.CommonMaterials,
            request.ProgressReporter,
            cancellationToken);
        await commonMaterialSetupAssetPreparer.PrepareAsync(
            routedClient,
            sceneSetup.SetupState,
            materials,
            request.CommonMaterials,
            progress,
            request.ProgressReporter,
            cancellationToken);

        ReportProgress(
            request.ProgressReporter,
            PlateauLog.Info(
                "live",
                "setup fixed dataset license metadata/component before city-object streaming starts."));
        ReportProgress(
            request.ProgressReporter,
            PlateauLog.Info(
                "live",
                $"Dataset metadata/license phase complete during setup. "
                + $"Dataset root existed={sceneSetup.SetupState.DatasetRootExisted}."));
        LiveSendRunState state = runStateFactory.Create(
            runPlan,
            sceneSetup.SetupState,
            progress,
            materials,
            sceneSetup.Placement,
            textureImageLoader,
            cancellationToken);
        workerLauncher.Start(
            state,
            request.ConnectionCount,
            request.Endpoint,
            request.ProgressReporter,
            request.ProcessQueuedCityObjectAsync,
            request.Diagnostics);
        return state;
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
