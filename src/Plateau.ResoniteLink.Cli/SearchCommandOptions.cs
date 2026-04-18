namespace Plateau.ResoniteLink.Cli;

public sealed record SearchCommandOptions(
    string LocalSourcePath,
    string MeshCode,
    IReadOnlyList<string>? PackageNames,
    CliOutputFormat OutputFormat) : CliCommandOptions;
