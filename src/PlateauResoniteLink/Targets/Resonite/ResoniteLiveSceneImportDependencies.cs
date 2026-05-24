using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Execution.IResoniteSceneSetupInterpreter SceneSetupInterpreter,
    IResoniteCommonMaterialSetupPreparer CommonMaterialSetupPreparer,
    ILiveSendRunPlanFactory RunPlanFactory,
    ILiveSendRunStateFactory RunStateFactory,
    IResoniteQueuedCityObjectWorker QueuedCityObjectWorker,
    IResoniteQueuedCityObjectEnqueuer QueuedCityObjectEnqueuer,
    IResoniteLiveSendFinalizer Finalizer,
    Execution.IResoniteSlotCreator SlotCreator,
    IResoniteBufferedCityObjectBakerFactory CityObjectBakerFactory);
