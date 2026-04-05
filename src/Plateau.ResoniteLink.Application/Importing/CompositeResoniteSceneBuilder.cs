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

        await Task.WhenAll(builders.Select(builder => builder.BeginAsync(metadata, outputRoot, cancellationToken)));
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        await Task.WhenAll(builders.Select(builder => builder.ProcessCityObjectAsync(cityObject, cancellationToken)));
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string>[] results = await Task.WhenAll(
            builders.Select(builder => builder.CompleteAsync(cancellationToken)));
        return results.SelectMany(static destinations => destinations).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(builders.Select(builder => builder.DisposeAsync().AsTask()));
    }
}
