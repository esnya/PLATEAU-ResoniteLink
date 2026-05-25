using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectPreparer
{
    Task<PreparedCityObject> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken callerCancellationToken);
}

internal sealed class ResoniteQueuedCityObjectPreparer(
    IResoniteQueuedGeometryPreparer geometryPreparer,
    IResoniteQueuedTexturePreparer texturePreparer) : IResoniteQueuedCityObjectPreparer
{
    private readonly IResoniteQueuedGeometryPreparer geometryPreparer =
        geometryPreparer ?? throw new ArgumentNullException(nameof(geometryPreparer));
    private readonly IResoniteQueuedTexturePreparer texturePreparer =
        texturePreparer ?? throw new ArgumentNullException(nameof(texturePreparer));

    public Task<PreparedCityObject> PrepareAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken callerCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectPreparationStartedLogged, 1, 0) == 0)
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"City object preparation started after {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"mesh='{cityObject.ActualMeshCode}'."));
        }

        CancellationToken processingCancellationToken = state.Runtime.ProcessingCancellationToken;
        return PrepareWithLinkedCancellationAsync(
            state,
            routedClient,
            cityObject,
            diagnostics,
            progressReporter,
            callerCancellationToken,
            processingCancellationToken);
    }

    private async Task<PreparedCityObject> PrepareWithLinkedCancellationAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken callerCancellationToken,
        CancellationToken processingCancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            processingCancellationToken);
        return await PrepareCoreAsync(
            state,
            routedClient,
            cityObject,
            diagnostics,
            progressReporter,
            linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCoreAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ResoniteQueuedGeometryPreparation geometryPreparation = geometryPreparer.Start(
            cityObject,
            cancellationToken);
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await texturePreparer.PrepareAsync(
            state,
            routedClient,
            geometryPreparation.CityObject,
            progressReporter,
            cancellationToken);
        PreparedQueuedGeometry preparedQueuedGeometry = await geometryPreparer.CompleteAsync(
            geometryPreparation,
            preparedTextures);
        cityObject = preparedQueuedGeometry.CityObject;
        PreparedConstructionGeometry preparedGeometry = preparedQueuedGeometry.Geometry;
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref state.Progress.FirstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                    + $"after scene start {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} "
                    + $"(textures={preparedTextures.Length}, geometry={PreparedConstructionGeometryFormatter.Describe(preparedGeometry)})."));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
