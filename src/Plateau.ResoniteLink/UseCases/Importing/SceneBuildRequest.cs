namespace Plateau.ResoniteLink.Application.Importing;

public sealed record SceneBuildRequest(
    ConstructionMetadata Metadata,
    IPlateauDatasetContentSource DatasetContentSource,
    string WorkRoot);
