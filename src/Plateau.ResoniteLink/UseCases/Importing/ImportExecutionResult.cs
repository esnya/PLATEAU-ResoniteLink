using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record ImportExecutionResult(
    ResoniteConstructionMetadata Metadata,
    IReadOnlyList<string> Destinations);
