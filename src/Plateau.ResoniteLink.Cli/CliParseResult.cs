namespace Plateau.ResoniteLink.Cli;

public sealed record CliParseResult(
    BuildCommandOptions? Options,
    string? Error,
    bool ShowHelp)
{
    public static CliParseResult Failure(string error)
    {
        return new CliParseResult(null, error, ShowHelp: false);
    }

    public static CliParseResult Help()
    {
        return new CliParseResult(null, null, ShowHelp: true);
    }

    public static CliParseResult Success(BuildCommandOptions options)
    {
        return new CliParseResult(options, null, ShowHelp: false);
    }
}
