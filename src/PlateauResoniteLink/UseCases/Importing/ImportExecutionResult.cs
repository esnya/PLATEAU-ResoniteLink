namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportExecutionResult(
    ImportedSceneMetadata Metadata,
    IReadOnlyList<string> Destinations);
