using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSendExecutionRuntimeTests
{
    [Fact]
    public async Task CancelRequestsProcessingCancellationSynchronously()
    {
        await using LiveSendExecutionRuntime runtime = new(
            new LiveSendQueuePlan(
                ConnectionCount: 1,
                QueueCapacity: 1,
                MemoryBudgetBytes: 1),
            CancellationToken.None);

        runtime.Cancel();

        Assert.True(runtime.IsCancellationRequested);
        Assert.True(runtime.ProcessingCancellationToken.IsCancellationRequested);
    }
}
