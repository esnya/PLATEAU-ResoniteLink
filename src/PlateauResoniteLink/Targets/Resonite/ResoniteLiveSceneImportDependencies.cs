using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportSession(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics);

internal sealed record ResoniteLiveSceneImportExecutionServices(
    IResoniteLiveSendExecutionGate ExecutionGate,
    IResoniteLiveSendRunStarter RunStarter,
    IResoniteImportedObjectUnitStreamQueueWriter ObjectUnitStreamQueueWriter,
    IResoniteLiveSendRunFinalizer RunFinalizer,
    IResoniteLiveSendExecutionResultFactory ExecutionResultFactory,
    IResoniteLiveSendRunResourceReleaser RunResourceReleaser,
    IResoniteQueuedCityObjectSender QueuedCityObjectSender);
