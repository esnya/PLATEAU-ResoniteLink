
using System;
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

    [Fact]
    public async Task AwaitCompletionAsyncPreservesProcessingFailureWhenCancellationCallbackThrows()
    {
        await using LiveSendExecutionRuntime runtime = new(
            new LiveSendQueuePlan(
                ConnectionCount: 1,
                QueueCapacity: 1,
                MemoryBudgetBytes: 1),
            CancellationToken.None);
        using CancellationTokenRegistration _ = runtime.ProcessingCancellationToken.Register(
            static () => throw new InvalidOperationException("cancel-callback-failed"));
        InvalidOperationException expectedFailure = new("processing-failed");

        runtime.Start([Task.Delay(Timeout.Infinite, runtime.ProcessingCancellationToken)]);
        runtime.TryMarkFailure(expectedFailure);

        InvalidOperationException actualFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AwaitCompletionAsync(CancellationToken.None));
        Assert.Same(expectedFailure, actualFailure);
    }

    [Fact]
    public async Task DisposeAsyncDoesNotThrowWhenCancellationCallbackThrows()
    {
        LiveSendExecutionRuntime runtime = new(
            new LiveSendQueuePlan(
                ConnectionCount: 1,
                QueueCapacity: 1,
                MemoryBudgetBytes: 1),
            CancellationToken.None);
        using CancellationTokenRegistration _ = runtime.ProcessingCancellationToken.Register(
            static () => throw new InvalidOperationException("cancel-callback-failed"));

        await runtime.DisposeAsync();
    }
}
