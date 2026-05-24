using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class LiveSendExecutionRuntimeTests
{
    [Fact]
    public async Task CancelRunsRegisteredCancellationCallbacksBeforeReturning()
    {
        await using LiveSendExecutionRuntime runtime = new(
            new LiveSendQueuePlan(
                ConnectionCount: 1,
                QueueCapacity: 1,
                MemoryBudgetBytes: 1),
            CancellationToken.None);
        bool callbackRan = false;
        using CancellationTokenRegistration _ = runtime.ProcessingCancellationToken.Register(
            () => callbackRan = true);

        runtime.Cancel();

        Assert.True(runtime.IsCancellationRequested);
        Assert.True(callbackRan);
    }
}
