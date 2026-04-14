using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class AsyncWeightedGateTests
{
    [Fact]
    public async Task AcquireAsyncWaitsUntilEnoughCapacityIsReleased()
    {
        AsyncWeightedGate gate = new(10);
        await using AsyncWeightedGate.Lease firstLease = await gate.AcquireAsync(8);

        Task<AsyncWeightedGate.Lease> secondAcquire = gate.AcquireAsync(4).AsTask();
        Assert.False(secondAcquire.IsCompleted);

        await firstLease.DisposeAsync();
        await using AsyncWeightedGate.Lease secondLease = await secondAcquire.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(4, gate.InUse);
    }

    [Fact]
    public async Task AcquireAsyncClampsSingleRequestToCapacity()
    {
        AsyncWeightedGate gate = new(10);

        await using AsyncWeightedGate.Lease lease = await gate.AcquireAsync(100);

        Assert.Equal(10, gate.InUse);
    }
}
