using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public sealed class ImportCommandOptions(
    PlateauImportRequest Request,
    string WorkRoot,
    ImportDestination Destination,
    PlateauImportMemoryProfile MemoryProfile,
    bool EnableMeshBake,
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache,
    bool EnableSendMetrics,
    bool VerboseLogging) : CliCommandOptions
{
    public PlateauImportRequest Request { get; } = Request ?? throw new ArgumentNullException(nameof(Request));

    public string WorkRoot { get; } = WorkRoot ?? throw new ArgumentNullException(nameof(WorkRoot));

    public ImportDestination Destination { get; } = Destination ?? throw new ArgumentNullException(nameof(Destination));

    public PlateauImportMemoryProfile MemoryProfile { get; } = MemoryProfile;

    public bool EnableMeshBake { get; } = EnableMeshBake;

    public string? TerrainTileCacheRoot { get; } = TerrainTileCacheRoot;

    public bool DisableTerrainTileCache { get; } = DisableTerrainTileCache;

    public bool EnableSendMetrics { get; } = EnableSendMetrics;

    public bool VerboseLogging { get; } = VerboseLogging;
}

public abstract record ImportDestination
{
    private ImportDestination()
    {
    }

    public sealed record Live : ImportDestination
    {
        public Live(Uri resoniteLinkUri, int connectionCount)
        {
            ArgumentNullException.ThrowIfNull(resoniteLinkUri);
            if (!resoniteLinkUri.IsAbsoluteUri)
            {
                throw new ArgumentException("The ResoniteLink URI must be absolute.", nameof(resoniteLinkUri));
            }

            if (connectionCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionCount), connectionCount, "Connection count must be positive.");
            }

            ResoniteLinkUri = resoniteLinkUri;
            ConnectionCount = connectionCount;
        }

        public Uri ResoniteLinkUri { get; }

        public int ConnectionCount { get; }
    }

    public sealed record CanonicalSceneDump : ImportDestination
    {
        public CanonicalSceneDump(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            Path = path;
        }

        public string Path { get; }
    }
}
