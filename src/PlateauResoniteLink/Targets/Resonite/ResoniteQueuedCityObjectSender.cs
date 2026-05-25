using System;
using System.Threading;
using System.Threading.Tasks;

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
    IResoniteQueuedCityObjectPreparer cityObjectPreparer,
    IResoniteQueuedSendFailurePolicy sendFailurePolicy,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteQueuedCityObjectSender
{
    private readonly IResoniteQueuedCityObjectPreparer cityObjectPreparer =
        cityObjectPreparer ?? throw new ArgumentNullException(nameof(cityObjectPreparer));
    private readonly IResoniteQueuedSendFailurePolicy sendFailurePolicy =
        sendFailurePolicy ?? throw new ArgumentNullException(nameof(sendFailurePolicy));
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
            PreparedCityObject preparedCityObject = await AwaitWithSlowCityObjectWarningAsync(
                cityObjectPreparer.PrepareAsync(
                    state,
                    routedClient,
                    queuedCityObject.CityObject,
                    diagnostics,
                    progressReporter,
                    cancellationToken),
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
            if (!sendFailurePolicy.IsRecoverable(exception))
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

    private static Task<T> AwaitWithSlowCityObjectWarningAsync<T>(
        Task<T> operationTask,
        CancellationToken cancellationToken)
    {
        return operationTask.WaitAsync(cancellationToken);
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
