namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportExecutionContext(
    ResoniteImportMemoryProfile MemoryProfile,
    int ConnectionCount,
    bool MeshBakeEnabled,
    ResoniteLiveSendTargetContext LiveSendContext);
