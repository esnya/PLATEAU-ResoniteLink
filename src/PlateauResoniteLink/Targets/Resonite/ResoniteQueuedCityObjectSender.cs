using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectSender
{
    Task SendAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteQueuedCityObjectSender(
    IResoniteQueuedCityObjectPreparation cityObjectPreparation,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteQueuedCityObjectSender
{
    private readonly IResoniteQueuedCityObjectPreparation cityObjectPreparation =
        cityObjectPreparation ?? throw new ArgumentNullException(nameof(cityObjectPreparation));
    private readonly IResonitePreparedCityObjectImporter preparedCityObjectImporter =
        preparedCityObjectImporter ?? throw new ArgumentNullException(nameof(preparedCityObjectImporter));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Live send should log and skip individual city object send failures while keeping the lane alive.")]
    public async Task SendAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(queuedCityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Interlocked.Increment(ref state.Progress.AttemptedCityObjectCount);
        try
        {
            PreparedCityObject preparedCityObject = await cityObjectPreparation.PrepareAsync(
                state,
                routedClient,
                queuedCityObject.CityObject,
                diagnostics,
                logger,
                cancellationToken);
            await preparedCityObjectImporter.ImportAsync(
                state,
                routedClient,
                queuedCityObject,
                preparedCityObject,
                diagnostics,
                logger,
                cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
            logger.WriteDebug(
                "Sent city object {ProcessedCount}: {DisplayName} ({PackageName}/{SlotKey})",
                processedCount,
                preparedCityObject.CityObject.DisplayName,
                preparedCityObject.CityObject.PackageName,
                preparedCityObject.CityObject.SlotKey);
            if (processedCount % 25 == 0)
            {
                logger.WriteInformation(
                    "Live send progress: attempted={AttemptedCount}, sent={SentCount}, failed={FailedCount}, queued_source={QueuedSourceCount}.",
                    state.Progress.AttemptedCityObjectCount,
                    processedCount,
                    state.Progress.FailedCityObjectCount,
                    state.Progress.QueuedCityObjectCount);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!IsRecoverableCityObjectSendFailure(exception))
            {
                throw;
            }

            int failedCount = Interlocked.Increment(ref state.Progress.FailedCityObjectCount);
            logger.WriteWarning(
                "Skipping city object after send failure {FailedCount}: {DisplayName} ({PackageName}/{SlotKey}). Reason: {Reason}",
                failedCount,
                queuedCityObject.CityObject.DisplayName,
                queuedCityObject.CityObject.PackageName,
                queuedCityObject.CityObject.SlotKey,
                exception.Message);
        }
        finally
        {
            await queuedCityObject.MemoryLease.DisposeAsync();
        }
    }

    private static bool IsRecoverableCityObjectSendFailure(Exception exception)
    {
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
