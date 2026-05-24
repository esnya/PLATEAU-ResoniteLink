using System;
using System.Threading;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendExecutionGate
{
    IDisposable Enter();
}

internal interface IResoniteLiveSendExecutionGateFactory
{
    IResoniteLiveSendExecutionGate Create();
}

internal sealed class ResoniteLiveSendExecutionGateFactory : IResoniteLiveSendExecutionGateFactory
{
    public IResoniteLiveSendExecutionGate Create()
    {
        return new ResoniteLiveSendExecutionGate();
    }
}

internal sealed class ResoniteLiveSendExecutionGate : IResoniteLiveSendExecutionGate
{
    private int executionClaimed;

    public IDisposable Enter()
    {
        if (Interlocked.Exchange(ref executionClaimed, 1) != 0)
        {
            throw new InvalidOperationException("A live scene import run is already active on this live scene import target instance.");
        }

        return new Lease(this);
    }

    private void Release()
    {
        Volatile.Write(ref executionClaimed, 0);
    }

    private sealed class Lease(
        ResoniteLiveSendExecutionGate gate) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
