using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResoniteSessionTool;

internal static class SessionToolPaths
{
    private const string DefaultTrackedStateFileName = "active-session.json";

    public static string ResolveRepoRoot(string repoPath)
    {
        return Path.GetFullPath(repoPath);
    }

    public static string ResolveHeadlessRuntimeRoot(string repoRoot)
    {
        return Path.Combine(repoRoot, "runtime", "windows", "headless");
    }

    public static string ResolveResoniteRuntimeRoot(string repoRoot)
    {
        return Path.Combine(repoRoot, "runtime", "windows", "resonite");
    }

    public static string? ResolveStatePath(string? configuredStatePath, string? repoRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredStatePath))
        {
            return Path.GetFullPath(configuredStatePath);
        }

        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return null;
        }

        return Path.Combine(ResolveHeadlessRuntimeRoot(repoRoot), DefaultTrackedStateFileName);
    }

    public static Uri ResolveEndpointFromState(string? statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
        {
            throw new InvalidOperationException($"No tracked headless session state file exists at '{statePath}', and no endpoint was provided.");
        }

        TrackedHeadlessSessionState state = ReadTrackedState(statePath);
        if (!Uri.TryCreate(state.Endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            throw new InvalidOperationException($"Tracked headless session state '{statePath}' does not contain a valid Endpoint.");
        }

        return endpoint;
    }

    public static string? ResolveDumpOutputPath(string? configuredOutputPath, string? repoRoot, string label)
    {
        if (!string.IsNullOrWhiteSpace(configuredOutputPath))
        {
            return Path.GetFullPath(configuredOutputPath);
        }

        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return null;
        }

        string dumpRoot = Path.Combine(ResolveResoniteRuntimeRoot(repoRoot), "root-dumps");
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(dumpRoot, $"{label}-{timestamp}.json");
    }

    public static string ResolveDotNetCommandPath()
    {
        foreach (string? candidate in EnumerateDotNetCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("Unable to locate dotnet.exe. Set DOTNET_EXE, DOTNET_HOST_PATH, or DOTNET_ROOT, or ensure dotnet is available on PATH.");
    }

    public static HeadlessLauncherSpec ResolveHeadlessLauncher(string configuredHeadlessPath)
    {
        string resolvedPath = Path.GetFullPath(configuredHeadlessPath);
        if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"The configured headless path '{configuredHeadlessPath}' does not exist.");
        }

        if (string.Equals(Path.GetFileName(resolvedPath), "Resonite", StringComparison.OrdinalIgnoreCase))
        {
            string headlessDirectory = Path.Combine(resolvedPath, "Headless");
            if (Directory.Exists(headlessDirectory))
            {
                resolvedPath = headlessDirectory;
            }
        }

        if (Directory.Exists(resolvedPath))
        {
            foreach (string candidateName in new[] { "Resonite.exe", "Resonite.dll" })
            {
                string candidatePath = Path.Combine(resolvedPath, candidateName);
                if (File.Exists(candidatePath))
                {
                    return new HeadlessLauncherSpec(
                        candidatePath,
                        resolvedPath,
                        candidateName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                }
            }

            throw new InvalidOperationException($"No Resonite launcher was found under '{resolvedPath}'. Expected Resonite.exe or Resonite.dll.");
        }

        string workingDirectory = Path.GetDirectoryName(resolvedPath)
            ?? throw new InvalidOperationException($"Cannot resolve a working directory for '{resolvedPath}'.");
        return new HeadlessLauncherSpec(
            resolvedPath,
            workingDirectory,
            resolvedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    public static TrackedHeadlessSessionState ReadTrackedState(string statePath)
    {
        string json = File.ReadAllText(statePath);
        TrackedHeadlessSessionState? state = JsonSerializer.Deserialize<TrackedHeadlessSessionState>(json);
        if (state is null)
        {
            throw new InvalidOperationException($"Tracked headless session state '{statePath}' could not be parsed.");
        }

        return state;
    }

    public static IEnumerable<string> GetCleanupArtifactPaths(string repoRoot)
    {
        string runtimeRoot = ResolveResoniteRuntimeRoot(repoRoot);
        return
        [
            Path.Combine(runtimeRoot, ".generated-assets"),
            Path.Combine(runtimeRoot, "resonite-live-asset-state.json"),
            Path.Combine(runtimeRoot, "resonite-live-asset-state.json.778de27fa819415a8310f8d02019bc12.tmp"),
        ];
    }

    public static string GetLogTail(string path, int lineCount = 20)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(lineCount));
    }

    public static string? FindLastMatchingLine(string path, Func<string, bool> predicate)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadLines(path).LastOrDefault(predicate);
    }

    public static int? TryExtractLastInt(string path, Regex pattern)
    {
        string? value = TryExtractLastString(path, pattern);
        return int.TryParse(value, out int parsed) ? parsed : null;
    }

    public static string? TryExtractLastString(string path, Regex pattern)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (string line in File.ReadLines(path).Reverse())
        {
            Match match = pattern.Match(line);
            if (match.Success && (match.Groups.Count > 1))
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    public static string ToWindowsCommandLine(string filePath, IReadOnlyList<string> argumentList)
    {
        List<string> segments = new(argumentList.Count + 1)
        {
            QuoteWindowsCommandLineArgument(filePath),
        };

        segments.AddRange(argumentList.Select(QuoteWindowsCommandLineArgument));
        return string.Join(' ', segments);
    }

    public static string QuoteWindowsCommandLineArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(static character => char.IsWhiteSpace(character) || (character == '"')))
        {
            return value;
        }

        StringBuilder builder = new();
        builder.Append('"');
        int backslashCount = 0;

        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(character);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string DescribeWin32Error(int errorCode)
    {
        return new Win32Exception(errorCode).Message;
    }

    private static IEnumerable<string?> EnumerateDotNetCandidates()
    {
        yield return Environment.GetEnvironmentVariable("DOTNET_EXE");
        yield return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "dotnet.exe");
        }

        foreach (string pathEntry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(pathEntry.Trim(), "dotnet.exe");
        }

        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "dotnet", "dotnet.exe");
        }
    }
}

internal sealed record HeadlessLauncherSpec(
    string LauncherPath,
    string WorkingDirectory,
    bool RequiresDotNetHost);

internal sealed record DiscoveryAnnouncement(
    string SessionName,
    string SessionId,
    int LinkPort,
    string RemoteIp,
    DateTimeOffset ReceivedAt);

internal sealed record TrackedHeadlessSessionState(
    int ProcessId,
    string SessionName,
    string SessionId,
    int LinkPort,
    string Endpoint,
    string DiscoveryMode,
    string ConfigPath,
    string SessionRoot,
    string StdoutLog,
    string StderrLog,
    string DataFolder,
    string CacheFolder,
    string LogsFolder,
    string LauncherPath,
    string WorkingDirectory,
    string WorldReadyLine,
    string StatePath);

internal sealed record ProcessStopResult(
    int ProcessId,
    bool WasRunning,
    bool HasExited,
    bool Forced);

internal sealed record RootDump(
    string Endpoint,
    DateTimeOffset CapturedAtUtc,
    int Depth,
    bool IncludeComponentData,
    object? Root);

internal sealed record SlotSummary(
    string Id,
    string Name);
