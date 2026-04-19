using System.Diagnostics;

namespace Plateau.ResoniteLink.Tests.Tools;

public sealed class ResoniteSessionToolCommandLineParserTests
{
    private static readonly string ScriptPath = TestData.GetRepositoryPath(
        ".agents",
        "skills",
        "resonite-live-send-debug",
        "tools",
        "session-tool.cs");

    [Fact]
    public async Task HelpListsThinPrimitiveSurface()
    {
        ProcessResult result = await RunSessionToolAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("discover-session", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("dump-slot", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("remove-slot", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("start-headless", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("stop-headless", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup-dataset-root", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DumpSlotRejectsMissingEndpointAndStateContext()
    {
        ProcessResult result = await RunSessionToolAsync("dump-slot", "--depth", "1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Provide an explicit endpoint or a valid --state-path/--runtime-root tracked state.", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveSlotRejectsConflictingSelectors()
    {
        ProcessResult result = await RunSessionToolAsync(
            "remove-slot",
            "ws://localhost:17136/",
            "--slot-id",
            "Root",
            "--root-child-name",
            "PLATEAU plateau-20202-matsumoto-shi-2020");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Use either --slot-id or --root-child-name, not both.", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartHeadlessRejectsDirectoryWithoutLauncherCandidates()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                Path.Combine(tempRoot, "runtime"),
                "--headless-path",
                tempRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Expected Resonite.dll, Resonite.exe, Headless/Resonite.dll, or Headless/Resonite.exe.", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StopHeadlessRejectsMissingLocator()
    {
        ProcessResult result = await RunSessionToolAsync("stop-headless");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("stop-headless requires --process-id or --runtime-root/--state-path.", result.StdErr, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunSessionToolAsync(params string[] sessionToolArgs)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = ResolveDotNetCommand(),
            WorkingDirectory = TestData.GetRepositoryPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add("--");

        foreach (string argument in sessionToolArgs)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string ResolveDotNetCommand()
    {
        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Environment.GetEnvironmentVariable("DOTNET_EXE")
            ?? "dotnet";
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
