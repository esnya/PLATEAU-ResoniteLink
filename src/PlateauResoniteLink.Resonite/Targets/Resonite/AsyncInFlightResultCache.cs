using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class AsyncInFlightResultCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> completedValues = [];
    private readonly ConcurrentDictionary<TKey, Task<TValue>> inFlightTasks = [];
    private readonly ConcurrentDictionary<TKey, SemaphoreSlim> gates = [];

    public async Task<TValue> GetOrCreateAsync(
        TKey key,
        Func<Task<TValue>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return await GetOrCreateAsync(
            key,
            _ => factory(),
            factoryCancellationToken: CancellationToken.None,
            cancellationToken);
    }

    public async Task<TValue> GetOrCreateAsync(
        TKey key,
        Func<CancellationToken, Task<TValue>> factory,
        CancellationToken factoryCancellationToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (completedValues.TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        Task<TValue> task = await GetOrCreateTaskAsync(key, factory, factoryCancellationToken, cancellationToken);
        return await task.WaitAsync(cancellationToken);
    }

    public void Clear()
    {
        completedValues.Clear();
        inFlightTasks.Clear();
        foreach ((_, SemaphoreSlim gate) in gates)
        {
            gate.Dispose();
        }

        gates.Clear();
    }

    public void Remember(TKey key, TValue value)
    {
        completedValues[key] = value;
        inFlightTasks.TryRemove(key, out _);
    }

    public bool TryGetCompleted(TKey key, out TValue? value)
    {
        return completedValues.TryGetValue(key, out value);
    }

    public void Remove(TKey key)
    {
        completedValues.TryRemove(key, out _);
        inFlightTasks.TryRemove(key, out _);
    }

    private async Task<Task<TValue>> GetOrCreateTaskAsync(
        TKey key,
        Func<CancellationToken, Task<TValue>> factory,
        CancellationToken factoryCancellationToken,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (completedValues.TryGetValue(key, out TValue? existingValue))
            {
                return Task.FromResult(existingValue);
            }

            if (inFlightTasks.TryGetValue(key, out Task<TValue>? existingTask))
            {
                return existingTask;
            }

            Task<TValue> createdTask = factory(factoryCancellationToken);
            inFlightTasks[key] = createdTask;
            ObserveCompletion(key, createdTask);
            return createdTask;
        }
        finally
        {
            gate.Release();
        }
    }

    private void ObserveCompletion(TKey key, Task<TValue> task)
    {
        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                CompletionState completionState = (CompletionState)state!;
                if (completedTask.Status == TaskStatus.RanToCompletion)
                {
                    completionState.Cache.completedValues[completionState.Key] = completedTask.Result;
                }

                if (completionState.Cache.inFlightTasks.TryGetValue(completionState.Key, out Task<TValue>? currentTask)
                    && ReferenceEquals(currentTask, completedTask))
                {
                    completionState.Cache.inFlightTasks.TryRemove(completionState.Key, out _);
                }
            },
            new CompletionState(this, key),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private readonly record struct CompletionState(
        AsyncInFlightResultCache<TKey, TValue> Cache,
        TKey Key);
}
