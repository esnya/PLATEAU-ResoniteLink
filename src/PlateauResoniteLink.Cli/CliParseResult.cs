using System;

namespace PlateauResoniteLink.Cli;

public abstract class CliParseResult
{
    private CliParseResult()
    {
    }

    public static CliParseResult Failure(string error)
    {
        return new FailureResult(error);
    }

    public static CliParseResult Help()
    {
        return HelpResult.Instance;
    }

    public static CliParseResult Success(ImportCommandOptions command)
    {
        return new ImportSuccessResult(command);
    }

    public static CliParseResult Success(SearchCommandOptions command)
    {
        return new SearchSuccessResult(command);
    }

    public static CliParseResult Success(StatsCommandOptions command)
    {
        return new StatsSuccessResult(command);
    }

    public sealed class ImportSuccessResult : CliParseResult
    {
        public ImportSuccessResult(ImportCommandOptions command)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public ImportCommandOptions Command { get; }
    }

    public sealed class SearchSuccessResult : CliParseResult
    {
        public SearchSuccessResult(SearchCommandOptions command)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public SearchCommandOptions Command { get; }
    }

    public sealed class StatsSuccessResult : CliParseResult
    {
        public StatsSuccessResult(StatsCommandOptions command)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public StatsCommandOptions Command { get; }
    }

    public sealed class FailureResult : CliParseResult
    {
        public FailureResult(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                throw new ArgumentException("CLI parse failure message must be provided.", nameof(error));
            }

            Error = error;
        }

        public string Error { get; }
    }

    public sealed class HelpResult : CliParseResult
    {
        public static HelpResult Instance { get; } = new();

        private HelpResult()
        {
        }
    }
}
