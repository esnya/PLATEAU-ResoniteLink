using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

public sealed record BuildCommandOptions(
    PlateauImportRequest Request,
    string WorkRoot,
    Uri? ResoniteLinkUri,
    int ResoniteLinkConnectionCount,
    int ResoniteLinkImportMeshTimeoutMilliseconds,
    bool EnableMeshBake,
    bool EnableSendMetrics,
    bool VerboseLogging);
