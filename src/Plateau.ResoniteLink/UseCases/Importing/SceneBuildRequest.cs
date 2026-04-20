namespace Plateau.ResoniteLink.Application.Importing;

public sealed record SceneBuildRequest(
    ImportedSceneMetadata Metadata,
    string ResolvedSourcePath,
    string WorkRoot);
