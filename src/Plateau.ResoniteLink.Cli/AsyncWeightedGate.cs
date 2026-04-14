namespace Plateau.ResoniteLink.Cli;

internal sealed class AsyncWeightedGate(long capacity)
{
    private readonly object syncRoot = new();
    private readonly Queue<Waiter> waiters = new();
    private long inUse;

    public long Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public long InUse
    {
        get
        {
            lock (syncRoot)
            {
                return inUse;
            }
        }
    }

    public ValueTask<Lease> AcquireAsync(long weight, CancellationToken cancellationToken = default)
    {
        long normalizedWeight = Math.Clamp(weight, 1L, Capacity);
        lock (syncRoot)
        {
            if (waiters.Count == 0 && inUse + normalizedWeight <= Capacity)
            {
                inUse += normalizedWeight;
                return ValueTask.FromResult(new Lease(this, normalizedWeight));
            }

            Waiter waiter = new(normalizedWeight);
            if (cancellationToken.CanBeCanceled)
            {
                waiter.RegisterCancellation(cancellationToken);
            }

            waiters.Enqueue(waiter);
            return new ValueTask<Lease>(waiter.Task);
        }
    }

    private void Release(long weight)
    {
        List<Waiter> readyWaiters = [];
        lock (syncRoot)
        {
            inUse = Math.Max(0L, inUse - weight);
            while (waiters.Count > 0)
            {
                Waiter waiter = waiters.Peek();
                if (waiter.IsCanceled)
                {
                    waiters.Dequeue();
                    continue;
                }

                if (inUse + waiter.Weight > Capacity)
                {
                    break;
                }

                waiters.Dequeue();
                inUse += waiter.Weight;
                readyWaiters.Add(waiter);
            }
        }

        foreach (Waiter waiter in readyWaiters)
        {
            waiter.TrySetResult(new Lease(this, waiter.Weight));
        }
    }

    internal struct Lease : IAsyncDisposable
    {
        private readonly AsyncWeightedGate gate;
        private readonly long weight;
        private readonly int valid;
        private int disposed;

        internal Lease(AsyncWeightedGate gate, long weight)
        {
            this.gate = gate;
            this.weight = weight;
            valid = 1;
            disposed = 0;
        }

        public ValueTask DisposeAsync()
        {
            if (valid == 0 || Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            gate.Release(weight);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Waiter(long weight)
    {
        private readonly TaskCompletionSource<Lease> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration cancellationRegistration;
        private int canceled;

        public long Weight => weight;

        public bool IsCanceled => Volatile.Read(ref canceled) != 0;

        public Task<Lease> Task => completionSource.Task;

        public void RegisterCancellation(CancellationToken cancellationToken)
        {
            cancellationRegistration = cancellationToken.Register(static state =>
            {
                Waiter waiter = (Waiter)state!;
                waiter.Cancel();
            }, this);
        }

        public void TrySetResult(Lease lease)
        {
            cancellationRegistration.Dispose();
            completionSource.TrySetResult(lease);
        }

        private void Cancel()
        {
            if (Interlocked.Exchange(ref canceled, 1) != 0)
            {
                return;
            }

            cancellationRegistration.Dispose();
            completionSource.TrySetCanceled();
        }
    }
}
