namespace Plateau.ResoniteLink.Cli;

public sealed record CliParseResult(
    CliCommandOptions? Command,
    string? Error,
    bool ShowHelp)
{
    public BuildCommandOptions? Options => Command as BuildCommandOptions;

    public static CliParseResult Failure(string error)
    {
        return new CliParseResult(null, error, ShowHelp: false);
    }

    public static CliParseResult Help()
    {
        return new CliParseResult(null, null, ShowHelp: true);
    }

    public static CliParseResult Success(CliCommandOptions command)
    {
        return new CliParseResult(command, null, ShowHelp: false);
    }
}
