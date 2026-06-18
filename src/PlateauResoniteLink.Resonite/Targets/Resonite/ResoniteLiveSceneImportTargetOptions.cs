using System;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

public sealed record ResoniteLiveSceneImportTargetOptions(
    Uri Endpoint,
    int ConnectionCount,
    bool EnableSendMetrics,
    ResoniteImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    bool EnableDistanceCulling = false);
