using System.Collections.ObjectModel;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

#pragma warning disable IDE0032

public sealed record ImportExecutionResult
{
    private ResoniteConstructionMetadata metadata = null!;
    private IReadOnlyList<string> destinations = Array.Empty<string>();

    public ImportExecutionResult(
        ResoniteConstructionMetadata metadata,
        IReadOnlyList<string> destinations)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(destinations);

        for (int index = 0; index < destinations.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(destinations[index], $"{nameof(destinations)}[{index}]");
        }

        Metadata = metadata;
        Destinations = destinations.Count == 0
            ? Array.Empty<string>()
            : new ReadOnlyCollection<string>([.. destinations]);
    }

    public ResoniteConstructionMetadata Metadata
    {
        get => metadata;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            metadata = value;
        }
    }

    public IReadOnlyList<string> Destinations
    {
        get => destinations;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            for (int index = 0; index < value.Count; index++)
            {
                ArgumentNullException.ThrowIfNull(value[index], $"{nameof(Destinations)}[{index}]");
            }

            destinations = value.Count == 0
                ? Array.Empty<string>()
                : new ReadOnlyCollection<string>([.. value]);
        }
    }

    public void Deconstruct(out ResoniteConstructionMetadata metadata, out IReadOnlyList<string> destinations)
    {
        metadata = Metadata;
        destinations = Destinations;
    }
}

#pragma warning restore IDE0032
