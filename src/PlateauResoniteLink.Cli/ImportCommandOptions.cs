using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public sealed record ImportCommandOptions(
    PlateauImportRequest Request,
    string WorkRoot,
    ImportTargetMode TargetMode,
    PlateauImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    bool EnableSendMetrics,
    bool VerboseLogging,
    bool EnableDistanceCulling = false) : CliCommandOptions;

public abstract record ImportTargetMode;

public sealed record LiveResoniteLinkImportMode : ImportTargetMode
{
    public LiveResoniteLinkImportMode(Uri endpoint, int connectionCount)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);

        Endpoint = endpoint;
        ConnectionCount = connectionCount;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }
}

public sealed record CanonicalSceneDumpImportMode : ImportTargetMode
{
    public CanonicalSceneDumpImportMode(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        OutputPath = outputPath;
    }

    public string OutputPath { get; }
}
