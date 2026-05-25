using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectSender
{
    Task SendAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
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
        Action<string>? progressReporter,
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
                progressReporter,
                cancellationToken);
            await preparedCityObjectImporter.ImportAsync(
                state,
                routedClient,
                queuedCityObject,
                preparedCityObject,
                diagnostics,
                progressReporter,
                cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"Sent city object {processedCount}: "
                    + $"{preparedCityObject.CityObject.DisplayName} "
                    + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})"));
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
            ReportProgress(
                progressReporter,
                PlateauLog.Warning(
                    "live",
                    $"Skipping city object after send failure {failedCount}: "
                    + $"{queuedCityObject.CityObject.DisplayName} "
                    + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}). "
                    + $"Reason: {exception.Message}"));
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

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
