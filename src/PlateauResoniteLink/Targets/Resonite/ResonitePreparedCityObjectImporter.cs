using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedCityObjectImporter
{
    Task ImportAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResonitePreparedCityObjectImporter(
    IResonitePreparedCityObjectAssetPlanner assetPlanner,
    IResoniteBatchEmissionPlanner batchEmissionPlanner,
    IResoniteSceneBatchEmitter batchEmitter) : IResonitePreparedCityObjectImporter
{
    private readonly IResonitePreparedCityObjectAssetPlanner assetPlanner =
        assetPlanner ?? throw new ArgumentNullException(nameof(assetPlanner));

    public async Task ImportAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(queuedCityObject);
        ArgumentNullException.ThrowIfNull(preparedCityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
        ReportImportStep(progressReporter, cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ResoniteObjectSlotHierarchy objectSlots = await AwaitWithSlowCityObjectWarningAsync(
            queuedCityObject.ObjectHierarchyTask,
            cancellationToken);
        slotHierarchyStopwatch.Stop();
        PreparedCityObjectAssetPlan assetPlan = await assetPlanner.PlanAsync(
            state,
            routedClient,
            preparedCityObject,
            message => ReportImportStep(progressReporter, cityObject, message),
            cancellationToken);

        PlannedSceneObjectEmission emissionPlan = new(
            assetPlan.GeometryAsset,
            assetPlan.Materials.MaterialAssets,
            new PlannedRenderer(
                assetPlan.GeometryAsset.Identity,
                assetPlan.Materials.RendererMaterialBindings),
            new PlannedCollider(
                assetPlan.GeometryAsset.Identity,
                cityObject.CollisionEnabled));
        PlannedBatchEmission batchEmission = batchEmissionPlanner.Create(objectSlots, emissionPlan);

        ReportImportStep(progressReporter, cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await batchEmitter.ExecuteAsync(
            routedClient,
            cityObject,
            batchEmission,
            progressReporter,
            cancellationToken);
        batchStopwatch.Stop();

        ReportImportStep(progressReporter, cityObject, "Live import completed.");
        cityObjectStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' phase timings: "
                + $"slot_hierarchy_s={slotHierarchyStopwatch.Elapsed.TotalSeconds:F3} "
                + $"geometry_assets_s={assetPlan.GeometryAssetSeconds:F3} "
                + $"materials_s={assetPlan.MaterialSeconds:F3} "
                + $"batch_s={batchStopwatch.Elapsed.TotalSeconds:F3} "
                + $"total_send_s={cityObjectStopwatch.Elapsed.TotalSeconds:F3}."));
        sendScope.MarkSent();
        if (Interlocked.CompareExchange(ref state.Progress.FirstImportedCityObjectLogged, 1, 0) == 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Debug(
                    "live",
                    $"First city object imported after {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})"));
        }
    }

    private static Task<T> AwaitWithSlowCityObjectWarningAsync<T>(
        Task<T> operationTask,
        CancellationToken cancellationToken)
    {
        return operationTask.WaitAsync(cancellationToken);
    }

    private static void ReportImportStep(
        Action<string>? progressReporter,
        ResoniteConstructionCityObject cityObject,
        string step)
    {
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"Importing '{cityObject.DisplayName}' ({cityObject.PackageName}/{cityObject.SlotKey}): {step}"));
    }
}
