using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLiveSceneImportExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_PreservesPrimaryFailureWhenCleanupFails()
    {
        InvalidOperationException primaryFailure = new("start failed");
        InvalidOperationException cleanupFailure = new("cleanup failed");
        RecordingResourceReleaser resourceReleaser = new()
        {
            Failure = cleanupFailure,
        };
        ResoniteLiveSceneImportExecutor executor = CreateExecutor(
            new ThrowingRunStarter(primaryFailure),
            resourceReleaser,
            new CompletingQueue());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(
                CreatePlan(),
                EmptyImportedObjectUnits(),
                CreateContext(),
                CancellationToken.None));

        Assert.Same(primaryFailure, failure);
        Assert.NotNull(resourceReleaser.Release);
        Assert.Equal(ResoniteLiveSendClientRelease.Reset, resourceReleaser.Release.ClientRelease);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCleanupFailureAfterSuccessfulImport()
    {
        InvalidOperationException cleanupFailure = new("cleanup failed");
        RecordingResourceReleaser resourceReleaser = new()
        {
            Failure = cleanupFailure,
        };
        ResoniteLiveSceneImportExecutor executor = CreateExecutor(
            new CompletingRunStarter(),
            resourceReleaser,
            new CompletingQueue());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(
                CreatePlan(),
                EmptyImportedObjectUnits(),
                CreateContext(),
                CancellationToken.None));

        Assert.Same(cleanupFailure, failure);
        Assert.NotNull(resourceReleaser.Release);
        Assert.Equal(ResoniteLiveSendClientRelease.None, resourceReleaser.Release.ClientRelease);
    }

    private static ResoniteLiveSceneImportExecutor CreateExecutor(
        IResoniteLiveSendRunStarter runStarter,
        IResoniteLiveSendResourceReleaser resourceReleaser,
        IResoniteLiveSendQueue queue)
    {
        return new ResoniteLiveSceneImportExecutor(
            new ResoniteLiveSendStartRequestFactory(),
            runStarter,
            new ResoniteLiveSendContextFactory(),
            resourceReleaser,
            queue);
    }

    private static SceneImportExecutionPlan CreatePlan()
    {
        using TemporaryDirectory datasetRoot = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetRoot.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0));

        return ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory.Path);
    }

    private static ResoniteLiveSceneImportExecutionContext CreateContext()
    {
        return new ResoniteLiveSceneImportExecutionContext(
            ResoniteImportMemoryProfile.Large,
            ConnectionCount: 1,
            MeshBakeEnabled: true,
            new ResoniteLiveSendTargetContext(
                new Uri("ws://localhost:12345/"),
                ConnectionCount: 1,
                new DelegatingClientSession(),
                ResoniteLinkSendDiagnostics.Disabled,
                ProgressReporter: null));
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> EmptyImportedObjectUnits()
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class ThrowingRunStarter(Exception failure) : IResoniteLiveSendRunStarter
    {
        public Task<LiveSendRunState> StartAsync(
            LiveSendRunStartRequest request,
            LiveSendRunStartContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromException<LiveSendRunState>(failure);
        }
    }

    private sealed class CompletingRunStarter : IResoniteLiveSendRunStarter
    {
        public Task<LiveSendRunState> StartAsync(
            LiveSendRunStartRequest request,
            LiveSendRunStartContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateRunState());
        }
    }

    private sealed class RecordingResourceReleaser : IResoniteLiveSendResourceReleaser
    {
        public Exception? Failure { get; init; }

        public ResoniteLiveSendResourceRelease? Release { get; private set; }

        public ValueTask ReleaseAsync(ResoniteLiveSendResourceRelease release)
        {
            Release = release;
            if (Failure is not null)
            {
                throw Failure;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompletingQueue : IResoniteLiveSendQueue
    {
        public Task QueueUnitAsync(
            LiveSendRunState state,
            ImportedObjectUnit objectUnit,
            LiveSendEnqueueContext context,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<SceneImportExecutionResult> CompleteAsync(
            LiveSendRunState state,
            LiveSendFinalizationContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new SceneImportExecutionResult(["ws://localhost:12345/"], 0));
        }
    }

    private static LiveSendRunState CreateRunState()
    {
        CreatedSlot datasetRootSlot = new(new ResoniteSlotLocator("dataset-root"), "Dataset");
        CreatedSlot datasetAssetsRootSlot = new(new ResoniteSlotLocator("dataset-assets-root"), "Assets");
        ResoniteLocalOrigin localOrigin = new(35.0, 139.0, 0.0);
        LiveSendQueuePlan queuePlan = new(ConnectionCount: 1, QueueCapacity: 1, MemoryBudgetBytes: 1);
        LiveSendRunPlan runPlan = new(
            new ResoniteSceneSetupInfo(
                "tokyo23ku",
                "53394525",
                SourceFiles: [],
                SelectedMeshCodes: [],
                new ResoniteLicenseAttributionMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid/license")),
            ResolvedWorkRoot: string.Empty,
            localOrigin,
            SourceFileSlotNamesByRelativePath: new Dictionary<string, string>(StringComparer.Ordinal),
            ResoniteImportBudgetProfiles.Large,
            queuePlan,
            MeshBakeEnabled: true);

        return new LiveSendRunState
        {
            Context = new LiveSendRunContext(
                runPlan,
                datasetRootSlot,
                datasetAssetsRootSlot,
                CommonAssetsRootSlot: new CreatedSlot(new ResoniteSlotLocator("common-assets-root"), "Common"),
                CityObjectBaker: null),
            Progress = new LiveSendProgressSink(),
            Materials = new CommonMaterialAssetCache(),
            TerrainTextures = new TerrainTextureAssetCache(),
            Placement = new ResoniteSharedSlotIndex(
                datasetRootSlot,
                datasetAssetsRootSlot,
                localOrigin,
                new Dictionary<string, string>(StringComparer.Ordinal),
                initialSceneAnchor: null,
                static (_, _, slotName, _, _, _) => Task.FromResult(new CreatedSlot(new ResoniteSlotLocator(slotName), slotName))),
            Runtime = new LiveSendExecutionRuntime(queuePlan, CancellationToken.None),
            GsiFallbackLicenseGate = new SemaphoreSlim(1, 1),
            DemSourceUseCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal),
        };
    }
}
