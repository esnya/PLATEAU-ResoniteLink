namespace Plateau.ResoniteLink.Application.Importing;

public sealed record SceneBuildRequest(
    ConstructionMetadata Metadata,
    string ResolvedSourcePath,
    string WorkRoot);
