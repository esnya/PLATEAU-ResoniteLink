using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteCityObjectQueueWriter
{
    Task QueueObjectUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        IResoniteLinkClient routedClient,
        int connectionCount,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);

    Task<int> FlushBufferedCityObjectsAsync(
        LiveSendRunState state,
        CompositeCityObjectBaker cityObjectBaker,
        IResoniteLinkClient routedClient,
        int connectionCount,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteCityObjectQueueWriter : IResoniteCityObjectQueueWriter
{
    public async Task QueueObjectUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        IResoniteLinkClient routedClient,
        int connectionCount,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(objectUnit);
        ArgumentNullException.ThrowIfNull(routedClient);

        foreach (ImportedCityObject cityObject in objectUnit.CityObjects)
        {
            await QueueCityObjectAsync(
                state,
                SceneImportContractMapper.ToInternal(cityObject),
                routedClient,
                progressReporter,
                cancellationToken);
        }

        CompositeCityObjectBaker? cityObjectBaker = state.Context.CityObjectBaker;
        if (cityObjectBaker is null)
        {
            return;
        }

        await FlushBufferedCityObjectsAsync(
            state,
            cityObjectBaker,
            routedClient,
            connectionCount,
            progressReporter,
            cancellationToken);
    }

    public async Task<int> FlushBufferedCityObjectsAsync(
        LiveSendRunState state,
        CompositeCityObjectBaker cityObjectBaker,
        IResoniteLinkClient routedClient,
        int connectionCount,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cityObjectBaker);
        ArgumentNullException.ThrowIfNull(routedClient);

        int bakedCityObjectCount = 0;
        List<Task> bakeEnqueueTasks = [];
        int maxInFlightBakeEnqueueTasks = Math.Max(4, connectionCount * 2);
        await cityObjectBaker.FlushAllAsync(
            async (bakedCityObject, callbackCancellationToken) =>
            {
                _ = Interlocked.Increment(ref bakedCityObjectCount);
                bakeEnqueueTasks.Add(EnqueueCityObjectAsync(
                    state,
                    ResoniteDynamicMaterialUvNormalizer.Normalize(bakedCityObject),
                    routedClient,
                    progressReporter,
                    callbackCancellationToken));
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

    private static async Task QueueCityObjectAsync(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        IResoniteLinkClient routedClient,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        CompositeCityObjectBaker? cityObjectBaker = state.Context.CityObjectBaker;
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
                await EnqueueCityObjectAsync(
                    state,
                    ResoniteDynamicMaterialUvNormalizer.Normalize(queuedCityObject),
                    routedClient,
                    progressReporter,
                    cancellationToken);
            }

            return;
        }

        await EnqueueCityObjectAsync(
            state,
            ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject),
            routedClient,
            progressReporter,
            cancellationToken);
    }

    private static async Task EnqueueCityObjectAsync(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        IResoniteLinkClient routedClient,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        await AwaitProcessingTasksIfCompletedAsync(state);

        LiveSendExecutionRuntime runtime = state.Runtime;
        long estimatedWorksetBytes = ResoniteCityObjectWorkingSetEstimator.Estimate(cityObject);
        AsyncWeightedGate.Lease cityObjectMemoryLease = await runtime.AcquireCityObjectMemoryAsync(
            estimatedWorksetBytes,
            cancellationToken);
        Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> objectHierarchyTask = CreateObjectHierarchyTask(
            state,
            cityObject,
            routedClient,
            cancellationToken);
        if (Interlocked.CompareExchange(ref state.Progress.FirstQueuedCityObjectLogged, 1, 0) == 0)
        {
            progressReporter?.Invoke(
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
                new QueuedCityObject(cityObject, objectHierarchyTask, cityObjectMemoryLease),
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

    private static Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> CreateObjectHierarchyTask(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        IResoniteLinkClient routedClient,
        CancellationToken callerCancellationToken)
    {
        CancellationToken processingCancellationToken = state.Runtime.ProcessingCancellationToken;
        return state.Placement.CreateObjectHierarchyTask(
            routedClient,
            cityObject,
            processingCancellationToken,
            callerCancellationToken);
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

    private static async Task AwaitProcessingTasksIfCompletedAsync(LiveSendRunState state)
    {
        await state.Runtime.AwaitIfAnyTaskCompletedAsync();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup should observe and suppress orphaned hierarchy task failures after the primary enqueue failure.")]
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
}
