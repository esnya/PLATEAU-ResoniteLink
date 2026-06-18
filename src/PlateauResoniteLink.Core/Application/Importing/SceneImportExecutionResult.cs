using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Application.Importing;

/// <summary>
/// Target-side execution summary for one scene import run.
/// ProcessedCityObjectCount represents successful sends, not source-side geometry availability.
/// </summary>
public sealed record SceneImportExecutionResult
{
    public SceneImportExecutionResult(
        IReadOnlyList<string> Destinations,
        int ProcessedCityObjectCount,
        int FailedCityObjectCount = 0)
        : this(Destinations, ProcessedCityObjectCount, FailedCityObjectCount, [])
    {
    }

    public SceneImportExecutionResult(
        IReadOnlyList<string> Destinations,
        int ProcessedCityObjectCount,
        IReadOnlyList<ImportDataSourceUsage> DataSourceUsages)
        : this(Destinations, ProcessedCityObjectCount, 0, DataSourceUsages)
    {
    }

    public SceneImportExecutionResult(
        IReadOnlyList<string> Destinations,
        int ProcessedCityObjectCount,
        int FailedCityObjectCount,
        IReadOnlyList<ImportDataSourceUsage> DataSourceUsages)
    {
        this.Destinations = Destinations;
        this.ProcessedCityObjectCount = ProcessedCityObjectCount;
        this.FailedCityObjectCount = FailedCityObjectCount;
        this.DataSourceUsages = DataSourceUsages;
    }

    public IReadOnlyList<string> Destinations { get; init; }

    public int ProcessedCityObjectCount { get; init; }

    public int FailedCityObjectCount { get; init; }

    public IReadOnlyList<ImportDataSourceUsage> DataSourceUsages { get; init; }
}
