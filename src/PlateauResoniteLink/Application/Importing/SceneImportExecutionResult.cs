using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

/// <summary>
/// Target-side execution summary for one scene import run.
/// ProcessedCityObjectCount represents successful sends, not source-side geometry availability.
/// </summary>
public sealed record SceneImportExecutionResult(
    IReadOnlyList<string> Destinations,
    int ProcessedCityObjectCount,
    int FailedCityObjectCount = 0,
    IReadOnlyList<ImportDataSourceUsage>? DataSourceUsages = null);
