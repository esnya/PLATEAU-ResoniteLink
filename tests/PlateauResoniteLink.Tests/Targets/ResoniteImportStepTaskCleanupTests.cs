using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteImportStepTaskCleanupTests
{
    [Fact]
    public async Task CancelAndObserveFailuresAsyncSuppressesCancellationCallbackFailures()
    {
        using CancellationTokenSource cancellation = new();
        using CancellationTokenRegistration _ = cancellation.Token.Register(
            static () => throw new InvalidOperationException("callback failed"));

        await ResoniteImportStepTaskCleanup.CancelAndObserveFailuresAsync(
            cancellation,
            [Task.CompletedTask]);

        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelAndObserveFailuresAsyncObservesAndSuppressesTaskFailures()
    {
        using CancellationTokenSource cancellation = new();
        Task failedTask = Task.FromException(new InvalidOperationException("secondary failure"));

        await ResoniteImportStepTaskCleanup.CancelAndObserveFailuresAsync(
            cancellation,
            [failedTask]);

        Assert.True(cancellation.IsCancellationRequested);
    }
}
