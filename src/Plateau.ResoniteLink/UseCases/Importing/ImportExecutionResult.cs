namespace Plateau.ResoniteLink.Application.Importing;

public sealed record ImportExecutionResult(
    ConstructionMetadata Metadata,
    IReadOnlyList<string> Destinations);
