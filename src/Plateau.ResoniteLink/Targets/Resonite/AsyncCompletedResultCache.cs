using System.Collections.Concurrent;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed class AsyncCompletedResultCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> completedValues = [];
    private readonly ConcurrentDictionary<TKey, SemaphoreSlim> gates = [];

    public async Task<TValue> GetOrCreateAsync(
        TKey key,
        Func<CancellationToken, Task<TValue>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (completedValues.TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        SemaphoreSlim gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (completedValues.TryGetValue(key, out existingValue))
            {
                return existingValue;
            }

            TValue createdValue = await factory(cancellationToken);
            completedValues[key] = createdValue;
            return createdValue;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Clear()
    {
        completedValues.Clear();
        foreach ((_, SemaphoreSlim gate) in gates)
        {
            gate.Dispose();
        }

        gates.Clear();
    }

    public void Remember(TKey key, TValue value)
    {
        completedValues[key] = value;
    }

    public void Remove(TKey key)
    {
        completedValues.TryRemove(key, out _);
    }
}
