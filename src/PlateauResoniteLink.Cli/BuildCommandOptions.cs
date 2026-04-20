using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public sealed record BuildCommandOptions(
    PlateauImportRequest Request,
    string WorkRoot,
    Uri? ResoniteLinkUri,
    int ResoniteLinkConnectionCount,
    PlateauImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    bool EnableSendMetrics,
    bool VerboseLogging) : CliCommandOptions;
