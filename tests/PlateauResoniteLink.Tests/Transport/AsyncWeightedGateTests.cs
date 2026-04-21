using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;


namespace PlateauResoniteLink.Tests.Transport;

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

    [Fact]
    public async Task AcquireAsyncSkipsCanceledWaiterWithoutLeakingCapacity()
    {
        AsyncWeightedGate gate = new(10);
        await using AsyncWeightedGate.Lease firstLease = await gate.AcquireAsync(8);
        using CancellationTokenSource canceledWaiter = new();

        Task<AsyncWeightedGate.Lease> canceledAcquire = gate.AcquireAsync(4, canceledWaiter.Token).AsTask();
        Task<AsyncWeightedGate.Lease> nextAcquire = gate.AcquireAsync(2).AsTask();

        await canceledWaiter.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledAcquire);

        await firstLease.DisposeAsync();
        await using AsyncWeightedGate.Lease nextLease = await nextAcquire.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, gate.InUse);
    }
}
