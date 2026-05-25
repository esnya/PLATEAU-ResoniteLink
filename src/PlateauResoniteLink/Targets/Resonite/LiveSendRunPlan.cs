using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunPlan(
    ResoniteSceneSetupInfo SetupInfo,
    string ResolvedWorkRoot,
    ResoniteLocalOrigin RequestLocalOrigin,
    IReadOnlyDictionary<string, string> SourceFileSlotNamesByRelativePath,
    ResoniteImportBudgetProfile ResourceBudget,
    LiveSendQueuePlan Queue,
    bool MeshBakeEnabled);

internal sealed record LiveSendQueuePlan(
    int ConnectionCount,
    int QueueCapacity,
    long MemoryBudgetBytes);
