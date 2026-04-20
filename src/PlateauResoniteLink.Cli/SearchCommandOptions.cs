namespace PlateauResoniteLink.Cli;

public sealed record SearchCommandOptions(
    string CityGmlSourcePath,
    string MeshCode,
    IReadOnlyList<string>? PackageNames,
    CliOutputFormat OutputFormat) : CliCommandOptions;
