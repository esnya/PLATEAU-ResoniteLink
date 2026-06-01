using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public sealed class ImportCommandOptions(
    PlateauImportRequest Request,
    string WorkRoot,
    Uri? ResoniteLinkUri,
    int ResoniteLinkConnectionCount,
    PlateauImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    string? CanonicalSceneDumpPath,
    bool EnableSendMetrics,
    bool VerboseLogging) : CliCommandOptions
{
    public PlateauImportRequest Request { get; } = Request ?? throw new ArgumentNullException(nameof(Request));

    public string WorkRoot { get; } = WorkRoot ?? throw new ArgumentNullException(nameof(WorkRoot));

    public Uri? ResoniteLinkUri { get; } = ResoniteLinkUri;

    public int ResoniteLinkConnectionCount { get; } = ResoniteLinkConnectionCount;

    public PlateauImportMemoryProfile MemoryProfile { get; } = MemoryProfile;

    public bool EnableMeshBake { get; } = EnableMeshBake;

    public string? TerrainTileCacheRoot { get; } = TerrainTileCacheRoot;

    public bool DisableTerrainTileCache { get; } = DisableTerrainTileCache;

    public string? CanonicalSceneDumpPath { get; } = CanonicalSceneDumpPath;

    public bool EnableSendMetrics { get; } = EnableSendMetrics;

    public bool VerboseLogging { get; } = VerboseLogging;
}
