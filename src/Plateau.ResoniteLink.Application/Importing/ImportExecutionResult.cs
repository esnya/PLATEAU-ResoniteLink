using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record ImportExecutionResult(
    ResoniteConstructionPlan Plan,
    IReadOnlyList<string> Destinations);
