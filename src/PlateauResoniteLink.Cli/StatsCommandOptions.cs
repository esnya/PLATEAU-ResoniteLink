namespace PlateauResoniteLink.Cli;

public sealed record StatsCommandOptions(
    string CityGmlSourcePath,
    IReadOnlyList<string>? PackageNames,
    CliOutputFormat OutputFormat) : CliCommandOptions;
