
using System;

using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class AsyncInFlightResultCacheTests
{
    [Fact]
    public async Task GetOrCreateAsyncInvokesFactoryOnlyOnceForConcurrentRequests()
    {
        PlateauResoniteLink.Resonite.Targets.Resonite.AsyncInFlightResultCache<string, int> cache = new();
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
        PlateauResoniteLink.Resonite.Targets.Resonite.AsyncInFlightResultCache<string, int> cache = new();
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

    [Fact]
    public async Task GetOrCreateAsyncPassesFactoryCancellationTokenToCreatedTask()
    {
        PlateauResoniteLink.Resonite.Targets.Resonite.AsyncInFlightResultCache<string, int> cache = new();
        using CancellationTokenSource factoryCancellation = new();
        TaskCompletionSource factoryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> request = cache.GetOrCreateAsync(
            "shared",
            async cancellationToken =>
            {
                factoryStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 42;
            },
            factoryCancellation.Token,
            CancellationToken.None);

        await factoryStarted.Task;
        await factoryCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
    }
}
