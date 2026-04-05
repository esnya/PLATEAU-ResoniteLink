using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IResoniteSceneBuilder
{
    Task<IReadOnlyList<string>> BuildAsync(
        ResoniteConstructionPlan plan,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
