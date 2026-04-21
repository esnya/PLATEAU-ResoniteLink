using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class AsyncCompletedResultCacheTests
{
    [Fact]
    public async Task GetOrCreateAsyncInvokesFactoryOnlyOnceForConcurrentRequests()
    {
        PlateauResoniteLink.Targets.Resonite.AsyncCompletedResultCache<string, int> cache = new();
        int invocationCount = 0;
        TaskCompletionSource releaseFactory = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int>[] requests =
        [
            cache.GetOrCreateAsync("shared", async _ =>
            {
                int currentCount = Interlocked.Increment(ref invocationCount);
                await releaseFactory.Task;
                return currentCount;
            }, CancellationToken.None),
            cache.GetOrCreateAsync("shared", async _ =>
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
}
