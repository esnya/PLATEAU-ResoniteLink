using System;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportTargetOptions(
    Uri Endpoint,
    int ConnectionCount,
    bool EnableSendMetrics,
    ResoniteImportMemoryProfile MemoryProfile,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    ILoggerFactory LoggerFactory);
