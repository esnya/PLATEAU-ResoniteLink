using System.Threading;
using System.Threading.Tasks;

using System.CommandLine;

namespace PlateauResoniteLink.Cli;

internal sealed class CliApplication(
    ICliRootCommandFactory commandFactory,
    CliConsoleWriters consoleWriters)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] effectiveArgs = args.Length == 0 ? ["--help"] : args;

        return await commandFactory
            .Create()
            .Parse(effectiveArgs)
            .InvokeAsync(
                new InvocationConfiguration
                {
                    Output = consoleWriters.StandardOutput,
                    Error = consoleWriters.StandardError,
                    EnableDefaultExceptionHandler = false,
                },
                cancellationToken);
    }
}
