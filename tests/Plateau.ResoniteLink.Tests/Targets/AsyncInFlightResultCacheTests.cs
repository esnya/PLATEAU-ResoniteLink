namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class AsyncInFlightResultCacheTests
{
    [Fact]
    public async Task GetOrCreateAsyncInvokesFactoryOnlyOnceForConcurrentRequests()
    {
        Plateau.ResoniteLink.Targets.Resonite.AsyncInFlightResultCache<string, int> cache = new();
        int invocationCount = 0;
        TaskCompletionSource releaseFactory = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int>[] requests =
        [
            cache.GetOrCreateAsync("shared", async () =>
            {
                int currentCount = Interlocked.Increment(ref invocationCount);
                await releaseFactory.Task;
                return currentCount;
            }, CancellationToken.None),
            cache.GetOrCreateAsync("shared", async () =>
            {
                int currentCount = Interlocked.Increment(ref invocationCount);
                await releaseFactory.Task;
                return currentCount;
            }, CancellationToken.None),
        ];

        releaseFactory.SetResult();
        int[] results = await Task.WhenAll(requests);

        Assert.Equal(1, Volatile.Read(ref invocationCount));
        Assert.Equal([1, 1], results);
    }

    [Fact]
    public async Task GetOrCreateAsyncKeepsInFlightTaskWhenFirstWaiterIsCanceled()
    {
        Plateau.ResoniteLink.Targets.Resonite.AsyncInFlightResultCache<string, int> cache = new();
        int invocationCount = 0;
        TaskCompletionSource releaseFactory = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource firstWaiterCancellation = new();

        Task<int> canceledRequest = cache.GetOrCreateAsync("shared", async () =>
        {
            Interlocked.Increment(ref invocationCount);
            await releaseFactory.Task;
            return 42;
        }, firstWaiterCancellation.Token);

        await firstWaiterCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledRequest);

        Task<int> reusedRequest = cache.GetOrCreateAsync("shared", async () =>
        {
            Interlocked.Increment(ref invocationCount);
            await Task.Yield();
            return 99;
        }, CancellationToken.None);

        releaseFactory.SetResult();
        int result = await reusedRequest;

        Assert.Equal(1, Volatile.Read(ref invocationCount));
        Assert.Equal(42, result);
    }
}
