using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class CompositeResoniteSceneBuilder(IReadOnlyList<IResoniteSceneBuilder> builders)
    : IResoniteSceneBuilder
{
    private readonly IReadOnlyList<IResoniteSceneBuilder> builders = builders;

    public async Task<IReadOnlyList<string>> BuildAsync(
        ResoniteConstructionPlan plan,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        List<string> destinations = [];

        foreach (IResoniteSceneBuilder builder in builders)
        {
            IReadOnlyList<string> builderDestinations =
                await builder.BuildAsync(plan, outputRoot, cancellationToken);

            destinations.AddRange(builderDestinations);
        }

        return destinations;
    }
}
