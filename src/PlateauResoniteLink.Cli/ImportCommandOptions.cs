using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public sealed record ImportCommandOptions(
    PlateauImportRequest Request,
    string WorkRoot,
    Uri? ResoniteLinkUri,
    int ResoniteLinkConnectionCount,
    PlateauImportMemoryProfile MemoryProfile,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    string? CanonicalSceneDumpPath,
    bool EnableSendMetrics,
    bool VerboseLogging) : CliCommandOptions;
