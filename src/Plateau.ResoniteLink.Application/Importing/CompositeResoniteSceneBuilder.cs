using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class CompositeResoniteSceneBuilder(IReadOnlyList<IResoniteSceneBuilder> builders)
    : IResoniteSceneBuilder
{
    private readonly IReadOnlyList<IResoniteSceneBuilder> builders = builders;

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        foreach (IResoniteSceneBuilder builder in builders)
        {
            await builder.BeginAsync(metadata, outputRoot, cancellationToken);
        }
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        foreach (IResoniteSceneBuilder builder in builders)
        {
            await builder.ProcessCityObjectAsync(cityObject, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        List<string> destinations = [];

        foreach (IResoniteSceneBuilder builder in builders)
        {
            IReadOnlyList<string> builderDestinations = await builder.CompleteAsync(cancellationToken);
            destinations.AddRange(builderDestinations);
        }

        return destinations;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IResoniteSceneBuilder builder in builders)
        {
            await builder.DisposeAsync();
        }
    }
}
