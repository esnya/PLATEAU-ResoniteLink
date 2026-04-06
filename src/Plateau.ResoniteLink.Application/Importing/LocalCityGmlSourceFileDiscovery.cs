using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class LocalCityGmlSourceFileDiscovery
{
    private static readonly Regex MeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8}|\d{6})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<LocalCityGmlSourceFileDescriptor> Discover(
        string datasetRoot,
        string meshCode,
        IReadOnlyList<string>? packageNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        string[] sourceFileSearchCodes = GetSourceFileSearchCodes(meshCode);
        HashSet<string>? requestedPackageNames = packageNames is null
            ? null
            : new HashSet<string>(
                PlateauPackageCatalog.NormalizeRequestedPackageNames(packageNames),
                StringComparer.OrdinalIgnoreCase);

        return Directory
            .EnumerateFiles(datasetRoot, "*.gml", SearchOption.AllDirectories)
            .Select(path => CreateSourceFileDescriptor(datasetRoot, path, sourceFileSearchCodes, requestedPackageNames))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .OrderBy(static descriptor => GetPackageSendPriority(descriptor.PackageName))
            .ThenBy(static descriptor => descriptor.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetSourceFileSearchCodes(string meshCode)
    {
        if (meshCode.Length >= 8)
        {
            return [meshCode, meshCode[..6]];
        }

        return [meshCode];
    }

    private static LocalCityGmlSourceFileDescriptor? CreateSourceFileDescriptor(
        string datasetRoot,
        string path,
        string[] sourceFileSearchCodes,
        HashSet<string>? requestedPackageNames)
    {
        string relativePath = NormalizePath(Path.GetRelativePath(datasetRoot, path));
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[0], "udx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!PlateauPackageCatalog.TryNormalizePackageName(segments[1], out string packageName))
        {
            return null;
        }

        if (requestedPackageNames is not null && !requestedPackageNames.Contains(packageName))
        {
            return null;
        }

        string? matchedMeshCode = MatchMeshCodeFromFileName(path, sourceFileSearchCodes)
            ?? MatchMeshCodeFromDirectoryPath(segments, sourceFileSearchCodes);
        if (matchedMeshCode is null)
        {
            return null;
        }

        return new LocalCityGmlSourceFileDescriptor(
            path,
            relativePath,
            packageName,
            matchedMeshCode.Length < sourceFileSearchCodes[0].Length);
    }

    private static string? MatchMeshCodeFromFileName(string path, string[] sourceFileSearchCodes)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        string[] fileMeshCodes = MeshCodeTokenRegex
            .Matches(fileNameWithoutExtension)
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return sourceFileSearchCodes
            .OrderByDescending(static code => code.Length)
            .FirstOrDefault(code => fileMeshCodes.Contains(code, StringComparer.Ordinal));
    }

    private static string? MatchMeshCodeFromDirectoryPath(string[] segments, string[] sourceFileSearchCodes)
    {
        string[] directorySegments = segments
            .Skip(2)
            .Take(Math.Max(segments.Length - 3, 0))
            .ToArray();

        return sourceFileSearchCodes
            .OrderByDescending(static code => code.Length)
            .FirstOrDefault(code => directorySegments.Contains(code, StringComparer.OrdinalIgnoreCase));
    }

    private static int GetPackageSendPriority(string packageName)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }
}

public sealed record LocalCityGmlSourceFileDescriptor(
    string AbsolutePath,
    string RelativePath,
    string PackageName,
    bool RequiresMeshAreaFilter);
