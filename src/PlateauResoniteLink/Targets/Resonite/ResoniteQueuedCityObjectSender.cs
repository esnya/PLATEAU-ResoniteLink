using System;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Transport.ResoniteLink;
using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteQueuedCityObjectSender(
    ResoniteQueuedCityObjectPreparation cityObjectPreparation,
    ResonitePreparedCityObjectImporter preparedCityObjectImporter)
{
    private readonly ResoniteQueuedCityObjectPreparation cityObjectPreparation =
        cityObjectPreparation ?? throw new ArgumentNullException(nameof(cityObjectPreparation));
    private readonly ResonitePreparedCityObjectImporter preparedCityObjectImporter =
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
                diagnostics, cancellationToken);
            await preparedCityObjectImporter.ImportAsync(
                state,
                routedClient,
                queuedCityObject,
                preparedCityObject,
                diagnostics, cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
            PlateauDiagnostics.Verbose(
                "Sent city object {ProcessedCount}: {DisplayName} ({PackageName}/{SlotKey})",
                processedCount,
                preparedCityObject.CityObject.DisplayName,
                preparedCityObject.CityObject.PackageName,
                preparedCityObject.CityObject.SlotKey);
            if (processedCount % 25 == 0)
            {
                int queuedCount = state.Progress.QueuedCityObjectCount;
                PlateauDiagnostics.Progress(
                    "Live send progress: phase=sending, source_city_objects_seen={SourceCityObjectCount}, sent={SentCount}, failed={FailedCount}, attempted={AttemptedCount}, queued={QueuedSourceCount}, backlog={BacklogCount}.",
                    state.Progress.SourceCityObjectCount,
                    processedCount,
                    state.Progress.FailedCityObjectCount,
                    state.Progress.AttemptedCityObjectCount,
                    queuedCount,
                    Math.Max(0, queuedCount - processedCount - state.Progress.FailedCityObjectCount));
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
            PlateauDiagnostics.Warning(
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
