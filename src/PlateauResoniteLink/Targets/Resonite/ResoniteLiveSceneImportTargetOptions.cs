using System;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportTargetOptions(
    Uri Endpoint,
    int ConnectionCount,
    bool EnableSendMetrics,
    ResoniteImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    ILoggerFactory LoggerFactory,
    bool EnableDistanceCulling = false);
