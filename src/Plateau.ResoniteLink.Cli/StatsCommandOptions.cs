namespace Plateau.ResoniteLink.Cli;

public sealed record StatsCommandOptions(
    string LocalSourcePath,
    IReadOnlyList<string>? PackageNames,
    CliOutputFormat OutputFormat) : CliCommandOptions;
