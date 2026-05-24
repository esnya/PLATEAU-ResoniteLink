using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneSink
{
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly IResoniteQueuedCityObjectSender queuedCityObjectSender;
    private readonly IResoniteLiveSendRunFinalizer runFinalizer;
    private readonly IResoniteLiveSendExecutionResultFactory executionResultFactory;
    private readonly IResoniteLiveSendRunResourceReleaser runResourceReleaser;
    private readonly IResoniteLiveSendExecutionGate executionGate;
    private readonly IResoniteLiveSendRunStarter runStarter;
    private readonly IResoniteImportedObjectUnitStreamQueueWriter objectUnitStreamQueueWriter;
#pragma warning disable CA1859
    private ILiveSendClientSession ClientSessionInternal { get; }
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;

    internal ResoniteLiveSceneImportTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLiveSceneImportSession session,
        ResoniteLiveSceneImportExecutionServices executionServices)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(executionServices);
        ArgumentNullException.ThrowIfNull(session.ClientSession);
        ArgumentNullException.ThrowIfNull(executionServices.ExecutionGate);
        ArgumentNullException.ThrowIfNull(executionServices.RunStarter);
        ArgumentNullException.ThrowIfNull(executionServices.ObjectUnitStreamQueueWriter);
        ArgumentNullException.ThrowIfNull(executionServices.RunFinalizer);
        ArgumentNullException.ThrowIfNull(executionServices.ExecutionResultFactory);
        ArgumentNullException.ThrowIfNull(executionServices.RunResourceReleaser);
        ArgumentNullException.ThrowIfNull(executionServices.QueuedCityObjectSender);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = session.Diagnostics;
        executionGate = executionServices.ExecutionGate;
        runStarter = executionServices.RunStarter;
        objectUnitStreamQueueWriter = executionServices.ObjectUnitStreamQueueWriter;
        runFinalizer = executionServices.RunFinalizer;
        executionResultFactory = executionServices.ExecutionResultFactory;
        runResourceReleaser = executionServices.RunResourceReleaser;
        queuedCityObjectSender = executionServices.QueuedCityObjectSender;
        MeshBakeEnabled = options.EnableMeshBake;
        progressReporter = options.ProgressReporter;
        ClientSessionInternal = session.ClientSession;
    }

    internal bool MeshBakeEnabled { get; }

    internal ResoniteLinkSendDiagnostics Diagnostics { get; }

    internal ILiveSendClientSession ClientSession => ClientSessionInternal;

    internal ResoniteImportMemoryProfile MemoryProfile { get; }

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(objectUnits);
        IDisposable executionLease = executionGate.Enter();
        bool completedSuccessfully = false;
        LiveSendRunState? state = null;

        try
        {
            SceneImportRequest request = plan.SceneImportRequest;
            state = await CreateRunStateAsync(
                CreateSceneSetupInfo(request),
                request.WorkRoot,
                request.CommonMaterials,
                plan.NormalizedRequest,
                CreateLocalOrigin(plan.SceneImportRequest.Metadata.GeodeticOrigin),
                cancellationToken);

            await objectUnitStreamQueueWriter.QueueAsync(
                state,
                objectUnits,
                GetRoutedClient(),
                connectionCount,
                progressReporter,
                cancellationToken);

            IReadOnlyList<string> destinations = await runFinalizer.CompleteAsync(
                state,
                GetRoutedClient(),
                endpoint,
                connectionCount,
                Diagnostics,
                progressReporter,
                cancellationToken);
            completedSuccessfully = true;
            return executionResultFactory.Create(destinations, state);
        }
        finally
        {
            try
            {
                await ReleaseRunResourcesAsync(
                    state,
                    disposeClients: false,
                    resetClients: !completedSuccessfully);
            }
            finally
            {
                executionLease.Dispose();
            }
        }
    }

    private async Task<LiveSendRunState> CreateRunStateAsync(
        ResoniteSceneSetupInfo SetupInfo,
        string workRoot,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        PlateauImportRequest normalizedRequest,
        ResoniteLocalOrigin requestLocalOrigin,
        CancellationToken cancellationToken)
    {
        return await runStarter.StartAsync(
            new LiveSendRunStartRequest(
                SetupInfo,
                workRoot,
                commonMaterials,
                normalizedRequest,
                requestLocalOrigin,
                ClientSessionInternal,
                endpoint,
                connectionCount,
                MemoryProfile,
                MeshBakeEnabled,
                Diagnostics,
                progressReporter,
                ProcessQueuedCityObjectAsync),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseRunResourcesAsync(
            state: null,
            disposeClients: true,
            resetClients: false);
    }

    private async ValueTask ReleaseRunResourcesAsync(
        LiveSendRunState? state,
        bool disposeClients,
        bool resetClients)
    {
        await runResourceReleaser.ReleaseAsync(
            state,
            ClientSessionInternal,
            disposeClients,
            resetClients);
    }

    private async Task ProcessQueuedCityObjectAsync(
        LiveSendRunState state,
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        await queuedCityObjectSender.SendAsync(
            state,
            GetRoutedClient(),
            queuedCityObject,
            Diagnostics,
            progressReporter,
            cancellationToken);
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return ClientSessionInternal.GetRequiredClient();
    }

    private static ResoniteSceneSetupInfo CreateSceneSetupInfo(SceneImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResoniteSceneSetupInfo(
            request.Metadata.Request.Dataset,
            request.Metadata.Request.MeshCode,
            request.Metadata.SourceDataset.SourceFiles,
            request.Metadata.SourceDataset.SelectedMeshCodes ?? [],
            new ResoniteLicenseAttributionMetadata(
                request.Metadata.Attribution.DatasetLicense.RequireCredit,
                request.Metadata.Attribution.DatasetLicense.CreditText,
                request.Metadata.Attribution.DatasetLicense.LicenseName,
                request.Metadata.Attribution.DatasetLicense.LicenseUrl));
    }

    private static ResoniteLocalOrigin CreateLocalOrigin(GeodeticOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new ResoniteLocalOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
    }

}
