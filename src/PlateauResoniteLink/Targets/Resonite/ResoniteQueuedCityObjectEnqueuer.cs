using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteQueuedCityObjectEnqueuer
{
    public static async Task QueueUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(objectUnit);
        ArgumentNullException.ThrowIfNull(context);

        foreach (ImportedCityObject cityObject in objectUnit.CityObjects)
        {
            await QueueAsync(
                state,
                SceneImportContractMapper.ToInternal(cityObject),
                context,
                cancellationToken);
        }

        NonDemCityObjectBaker? cityObjectBaker = state.Context.CityObjectBaker;
        if (cityObjectBaker is null)
        {
            return;
        }

        await FlushBufferedAsync(state, cityObjectBaker, context, cancellationToken);
    }

    public static async Task<int> FlushBufferedAsync(
        LiveSendRunState state,
        NonDemCityObjectBaker cityObjectBaker,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cityObjectBaker);
        ArgumentNullException.ThrowIfNull(context);

        int bakedCityObjectCount = 0;
        List<Task> bakeEnqueueTasks = [];
        int maxInFlightBakeEnqueueTasks = Math.Max(4, context.ConnectionCount * 2);
        await cityObjectBaker.FlushAllAsync(
            async (bakedCityObject, callbackCancellationToken) =>
            {
                _ = Interlocked.Increment(ref bakedCityObjectCount);
                bakeEnqueueTasks.Add(EnqueueAsync(state, bakedCityObject, context, callbackCancellationToken));
                if (bakeEnqueueTasks.Count >= maxInFlightBakeEnqueueTasks)
                {
                    await AwaitOneTaskSlotAsync(bakeEnqueueTasks, callbackCancellationToken);
                }
            },
            cancellationToken);
        if (bakeEnqueueTasks.Count > 0)
        {
            await Task.WhenAll(bakeEnqueueTasks).WaitAsync(cancellationToken);
        }

        return bakedCityObjectCount;
    }

    private static async Task QueueAsync(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        NonDemCityObjectBaker? cityObjectBaker = state.Context.CityObjectBaker;
        if (cityObjectBaker is not null)
        {
            IReadOnlyList<ResoniteConstructionCityObject> queuedCityObjects = await cityObjectBaker.BufferAsync(
                cityObject,
                cancellationToken);
            if (queuedCityObjects.Count == 0)
            {
                return;
            }

            foreach (ResoniteConstructionCityObject queuedCityObject in queuedCityObjects)
            {
                await EnqueueAsync(state, queuedCityObject, context, cancellationToken);
            }

            return;
        }

        await EnqueueAsync(state, cityObject, context, cancellationToken);
    }

    private static async Task EnqueueAsync(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken)
    {
        await AwaitProcessingTasksIfCompletedAsync(state);

        LiveSendExecutionRuntime runtime = state.Runtime;
        long estimatedWorksetBytes = ResoniteCityObjectWorkingSetEstimator.EstimateBytes(cityObject);
        AsyncWeightedGate.Lease cityObjectMemoryLease = await runtime.AcquireCityObjectMemoryAsync(
            estimatedWorksetBytes,
            cancellationToken);
        Task<ResoniteObjectSlotHierarchy> objectHierarchyTask = state.Placement.CreateObjectHierarchyTask(
            context.GetRoutedClient(),
            cityObject,
            runtime.ProcessingCancellationToken,
            cancellationToken);
        if (Interlocked.CompareExchange(ref state.Progress.FirstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"First city object queued after {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"estimated_workset_bytes={estimatedWorksetBytes}."));
        }

        using CancellationTokenSource enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            runtime.ProcessingCancellationToken);
        try
        {
            await runtime.WriteAsync(
                new LiveSendQueuedCityObject(cityObject, objectHierarchyTask, cityObjectMemoryLease),
                enqueueCancellation.Token);
        }
        catch (OperationCanceledException) when (runtime.IsCancellationRequested)
        {
            await cityObjectMemoryLease.DisposeAsync();
            await AwaitProcessingTasksIfCompletedAsync(state);
            throw;
        }
        catch
        {
            await cityObjectMemoryLease.DisposeAsync();
            _ = ObserveTaskFailureAsync(objectHierarchyTask);
            throw;
        }

        await AwaitProcessingTasksIfCompletedAsync(state);
    }

    private static async Task AwaitOneTaskSlotAsync(
        List<Task> tasks,
        CancellationToken cancellationToken)
    {
        for (int index = tasks.Count - 1; index >= 0; index--)
        {
            if (!tasks[index].IsCompleted)
            {
                continue;
            }

            Task completedTask = tasks[index];
            tasks.RemoveAt(index);
            await completedTask.WaitAsync(cancellationToken);
            return;
        }

        Task finishedTask = await Task.WhenAny(tasks).WaitAsync(cancellationToken);
        tasks.Remove(finishedTask);
        await finishedTask.WaitAsync(cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort enqueue cleanup should observe and suppress an orphaned hierarchy task failure after the primary enqueue failure.")]
    private static async Task ObserveTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static async Task AwaitProcessingTasksIfCompletedAsync(LiveSendRunState state)
    {
        await state.Runtime.AwaitIfAnyTaskCompletedAsync();
    }

    private static void ReportProgress(LiveSendEnqueueContext context, string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}

internal sealed record LiveSendEnqueueContext
{
    public LiveSendEnqueueContext(
        int ConnectionCount,
        Func<IResoniteLinkClient> GetRoutedClient,
        Action<string>? ProgressReporter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(GetRoutedClient);

        this.ConnectionCount = ConnectionCount;
        this.GetRoutedClient = GetRoutedClient;
        this.ProgressReporter = ProgressReporter;
    }

    public int ConnectionCount { get; }

    public Func<IResoniteLinkClient> GetRoutedClient { get; }

    public Action<string>? ProgressReporter { get; }
}
