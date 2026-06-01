using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Cli;

public sealed class StatsCommandOptions(
    string cityGmlSourcePath,
    IReadOnlyList<string>? packageNames,
    CliOutputFormat outputFormat) : CliCommandOptions
{
    public string CityGmlSourcePath { get; } = cityGmlSourcePath ?? throw new ArgumentNullException(nameof(cityGmlSourcePath));

    public IReadOnlyList<string>? PackageNames { get; } = packageNames;

    public CliOutputFormat OutputFormat { get; } = outputFormat;
}
