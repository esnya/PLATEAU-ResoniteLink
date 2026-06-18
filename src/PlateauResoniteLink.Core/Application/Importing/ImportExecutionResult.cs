using System.Collections.Generic;

using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Core.Application.Importing;

public sealed record ImportExecutionResult
{
    public ImportExecutionResult(
        ImportedSceneMetadata Metadata,
        IReadOnlyList<string> Destinations)
        : this(Metadata, Destinations, [])
    {
    }

    public ImportExecutionResult(
        ImportedSceneMetadata Metadata,
        IReadOnlyList<string> Destinations,
        IReadOnlyList<ImportDataSourceUsage> DataSourceUsages)
    {
        this.Metadata = Metadata;
        this.Destinations = Destinations;
        this.DataSourceUsages = DataSourceUsages;
    }

    public ImportedSceneMetadata Metadata { get; init; }

    public IReadOnlyList<string> Destinations { get; init; }

    public IReadOnlyList<ImportDataSourceUsage> DataSourceUsages { get; init; }
}
