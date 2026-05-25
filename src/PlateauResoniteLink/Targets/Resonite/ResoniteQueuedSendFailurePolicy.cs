using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedSendFailurePolicy
{
    bool IsRecoverable(Exception exception);
}

internal sealed class ResoniteQueuedSendFailurePolicy : IResoniteQueuedSendFailurePolicy
{
    public bool IsRecoverable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is ContinuableImportException
            || FindResoniteLinkOperationException(exception) is { OperationName: "ImportMesh" or "ImportTexture" or "GetSlot" or "GetComponent" };
    }

    private static ResoniteLinkOperationException? FindResoniteLinkOperationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ResoniteLinkOperationException operationException)
            {
                return operationException;
            }
        }

        return null;
    }
}
