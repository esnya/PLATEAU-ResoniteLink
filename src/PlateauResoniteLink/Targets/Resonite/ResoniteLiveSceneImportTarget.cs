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
    private readonly IResoniteLiveSendRunStarter runStarter;
    private readonly IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer;
    private readonly IResoniteLiveSendFinalizer finalizer;
#pragma warning disable CA1859
    private ILiveSendClientSession ClientSessionInternal { get; }
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;

    private int executionClaimed;

    internal ResoniteLiveSceneImportTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLiveSceneImportDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.RunStarter);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = dependencies.Diagnostics;
        MeshBakeEnabled = options.EnableMeshBake;
        progressReporter = options.ProgressReporter;
        runStarter = dependencies.RunStarter;
        queuedCityObjectEnqueuer = dependencies.QueuedCityObjectEnqueuer;
        finalizer = dependencies.Finalizer;
        ClientSessionInternal = dependencies.ClientSession;
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
        if (Interlocked.Exchange(ref executionClaimed, 1) != 0)
        {
            throw new InvalidOperationException("A live scene import run is already active on this live scene import target instance.");
        }
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

            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                await queuedCityObjectEnqueuer.QueueUnitAsync(
                    state,
                    objectUnit,
                    CreateEnqueueContext(),
                    cancellationToken);
            }

            SceneImportExecutionResult result = await finalizer.CompleteAsync(
                state,
                CreateFinalizationContext(),
                cancellationToken);
            completedSuccessfully = true;
            return result;
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
                Volatile.Write(ref executionClaimed, 0);
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
        ArgumentNullException.ThrowIfNull(SetupInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        return await runStarter.StartAsync(
            new LiveSendRunStartRequest(
                SetupInfo,
                workRoot,
                commonMaterials,
                normalizedRequest,
                requestLocalOrigin,
                MemoryProfile,
                connectionCount,
                MeshBakeEnabled),
            new LiveSendRunStartContext(
                endpoint,
                ClientSessionInternal,
                Diagnostics,
                progressReporter),
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
        if (state is not null)
        {
            await state.Runtime.DisposeAsync();
        }

        if (disposeClients)
        {
            ClientSessionInternal.DisposeClients();
        }
        else if (resetClients)
        {
            await ClientSessionInternal.ResetClientsAsync();
        }
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return ClientSessionInternal.GetRequiredClient();
    }

    private LiveSendEnqueueContext CreateEnqueueContext()
    {
        return new LiveSendEnqueueContext(
            connectionCount,
            GetRoutedClient,
            progressReporter);
    }

    private LiveSendFinalizationContext CreateFinalizationContext()
    {
        return new LiveSendFinalizationContext(
            endpoint,
            CreateEnqueueContext(),
            Diagnostics,
            progressReporter);
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
