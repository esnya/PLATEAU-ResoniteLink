using System;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public sealed record ImportRunCliOptions(string WorkRoot);

public sealed record ResoniteSceneBuildCliOptions(
    PlateauImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    bool EnableDistanceCulling = false);

public sealed record ResoniteLiveTransportCliOptions
{
    public ResoniteLiveTransportCliOptions(Uri endpoint, int connectionCount, bool enableSendMetrics)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);

        Endpoint = endpoint;
        ConnectionCount = connectionCount;
        EnableSendMetrics = enableSendMetrics;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }

    public bool EnableSendMetrics { get; }
}

public sealed record TerrainTileCacheCliOptions(
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache);

public sealed record CliDiagnosticsOptions(bool VerboseLogging);

public abstract record ImportSinkCliOptions;

public sealed record LiveResoniteSinkCliOptions(
    ResoniteLiveTransportCliOptions Transport,
    TerrainTileCacheCliOptions TerrainTileCache) : ImportSinkCliOptions;

public sealed record CanonicalSceneDumpSinkCliOptions : ImportSinkCliOptions
{
    public CanonicalSceneDumpSinkCliOptions(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        OutputPath = outputPath;
    }

    public string OutputPath { get; }
}
