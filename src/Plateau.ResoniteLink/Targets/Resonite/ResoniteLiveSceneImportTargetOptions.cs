using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

public sealed record ResoniteLiveSceneImportTargetOptions(
    Uri Endpoint,
    int ConnectionCount,
    bool EnableSendMetrics,
    PlateauImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    Action<string>? ProgressReporter);
