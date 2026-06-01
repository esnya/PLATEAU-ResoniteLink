using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Cli;

public sealed class SearchCommandOptions(
    string cityGmlSourcePath,
    string meshCode,
    IReadOnlyList<string>? packageNames,
    CliOutputFormat outputFormat) : CliCommandOptions
{
    public string CityGmlSourcePath { get; } = cityGmlSourcePath ?? throw new ArgumentNullException(nameof(cityGmlSourcePath));

    public string MeshCode { get; } = meshCode ?? throw new ArgumentNullException(nameof(meshCode));

    public IReadOnlyList<string>? PackageNames { get; } = packageNames;

    public CliOutputFormat OutputFormat { get; } = outputFormat;
}
