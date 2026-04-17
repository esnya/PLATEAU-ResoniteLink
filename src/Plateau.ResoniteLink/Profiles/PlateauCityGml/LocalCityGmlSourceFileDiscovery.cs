using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class LocalCityGmlSourceFileDiscovery
{
    private static readonly Regex MeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8}|\d{6})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MeshCodeSegmentRegex = new(
        @"^(?:\d{8}|\d{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LocalCityGmlSourceFileDiscoveryResult Discover(
        string datasetRoot,
        string meshCode,
        IReadOnlyList<string>? packageNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        MeshCodeRequestMatcher matcher = CreateMatcher(meshCode);
        HashSet<string>? requestedPackageNames = packageNames is null
            ? null
            : new HashSet<string>(
                PlateauPackageCatalog.NormalizeRequestedPackageNames(packageNames),
                StringComparer.OrdinalIgnoreCase);

        CandidateSourceFile[] candidates = Directory
            .EnumerateFiles(datasetRoot, "*.gml", SearchOption.AllDirectories)
            .Select(path => CreateCandidateSourceFile(datasetRoot, path, requestedPackageNames))
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .ToArray();

        return CreateSourceFileDiscoveryResult(candidates, matcher);
    }

    public static LocalCityGmlSourceFileDiscoveryResult Discover(
        IEnumerable<string> relativePaths,
        string meshCode,
        IReadOnlyList<string>? packageNames)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        MeshCodeRequestMatcher matcher = CreateMatcher(meshCode);
        HashSet<string>? requestedPackageNames = packageNames is null
            ? null
            : new HashSet<string>(
                PlateauPackageCatalog.NormalizeRequestedPackageNames(packageNames),
                StringComparer.OrdinalIgnoreCase);

        CandidateSourceFile[] candidates = relativePaths
            .Where(path => path.EndsWith(".gml", StringComparison.OrdinalIgnoreCase))
            .Select(path => CreateCandidateSourceFileFromRelativePath(path, requestedPackageNames))
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .ToArray();

        return CreateSourceFileDiscoveryResult(candidates, matcher);
    }

    private static MeshCodeRequestMatcher CreateMatcher(string meshCode)
    {
        if (PlateauMeshCode.TryGetBounds(meshCode, out _))
        {
            return meshCode.Length >= 8
                ? MeshCodeRequestMatcher.CreateExact(meshCode, [meshCode, meshCode[..6]])
                : MeshCodeRequestMatcher.CreateExact(meshCode, [meshCode]);
        }

        if (!MeshCodeInput.TryCreateRegex(meshCode, out Regex? regex, out string? error))
        {
            throw new ArgumentException(error, nameof(meshCode));
        }

        return MeshCodeRequestMatcher.CreateRegex(meshCode, regex!);
    }

    private static LocalCityGmlSourceFileDiscoveryResult CreateSourceFileDiscoveryResult(
        CandidateSourceFile[] candidates,
        MeshCodeRequestMatcher matcher)
    {
        string[] requestedMeshCodes = candidates
            .Where(static candidate => candidate.IsRequestedPackage)
            .SelectMany(static candidate => candidate.FileMeshCodes.Concat(candidate.DirectoryMeshCodes))
            .Distinct(StringComparer.Ordinal)
            .SelectMany(matcher.GetRequestedMeshCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static meshCode => meshCode, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> requestedMeshCodeSet = requestedMeshCodes.ToHashSet(StringComparer.Ordinal);
        HashSet<string> parentMeshCodes = requestedMeshCodes
            .Where(static matchedMeshCode => matchedMeshCode.Length == 8)
            .Select(static matchedMeshCode => matchedMeshCode[..6])
            .ToHashSet(StringComparer.Ordinal);

        LocalCityGmlSourceFileDescriptor[] sourceFiles = candidates
            .Select(candidate => CreateSourceFileDescriptor(candidate, matcher, requestedMeshCodeSet, parentMeshCodes))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .OrderBy(static descriptor => GetPackageSendPriority(descriptor.PackageName))
            .ThenBy(static descriptor => descriptor.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new LocalCityGmlSourceFileDiscoveryResult(sourceFiles, requestedMeshCodes);
    }

    private static CandidateSourceFile? CreateCandidateSourceFile(
        string datasetRoot,
        string path,
        HashSet<string>? requestedPackageNames)
    {
        string relativePath = NormalizePath(Path.GetRelativePath(datasetRoot, path));
        return CreateCandidateSourceFileCore(path, relativePath, requestedPackageNames);
    }

    private static CandidateSourceFile? CreateCandidateSourceFileFromRelativePath(
        string relativePath,
        HashSet<string>? requestedPackageNames)
    {
        string normalizedPath = NormalizePath(relativePath);
        return CreateCandidateSourceFileCore(normalizedPath, normalizedPath, requestedPackageNames);
    }

    private static CandidateSourceFile? CreateCandidateSourceFileCore(
        string absolutePath,
        string relativePath,
        HashSet<string>? requestedPackageNames)
    {
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

        string[] fileMeshCodes = ExtractMeshCodes(Path.GetFileNameWithoutExtension(relativePath));
        string[] directoryMeshCodes = segments
            .Skip(2)
            .Take(Math.Max(segments.Length - 3, 0))
            .Where(static segment => MeshCodeSegmentRegex.IsMatch(segment))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (fileMeshCodes.Length == 0 && directoryMeshCodes.Length == 0)
        {
            return null;
        }

        return new CandidateSourceFile(
            absolutePath,
            relativePath,
            packageName,
            requestedPackageNames is null || requestedPackageNames.Contains(packageName),
            fileMeshCodes,
            directoryMeshCodes);
    }

    private static LocalCityGmlSourceFileDescriptor? CreateSourceFileDescriptor(
        CandidateSourceFile candidate,
        MeshCodeRequestMatcher matcher,
        IReadOnlySet<string> requestedMeshCodes,
        IReadOnlySet<string> parentMeshCodes)
    {
        if (!candidate.IsRequestedPackage)
        {
            return null;
        }

        string? matchedMeshCode = matcher.Match(candidate.FileMeshCodes, requestedMeshCodes)
            ?? matcher.Match(candidate.DirectoryMeshCodes, requestedMeshCodes);
        if (matchedMeshCode is not null)
        {
            return new LocalCityGmlSourceFileDescriptor(
                candidate.AbsolutePath,
                candidate.RelativePath,
                candidate.PackageName,
                matchedMeshCode,
                matcher.RequiresMeshAreaFilter(matchedMeshCode));
        }

        string? parentMeshCode = candidate.FileMeshCodes
            .Concat(candidate.DirectoryMeshCodes)
            .Where(static meshCode => meshCode.Length == 6)
            .OrderByDescending(static meshCode => meshCode.Length)
            .ThenBy(static meshCode => meshCode, StringComparer.Ordinal)
            .FirstOrDefault(parentMeshCodes.Contains);
        if (parentMeshCode is null)
        {
            return null;
        }

        return new LocalCityGmlSourceFileDescriptor(
            candidate.AbsolutePath,
            candidate.RelativePath,
            candidate.PackageName,
            parentMeshCode,
            RequiresMeshAreaFilter: true);
    }

    private static string[] ExtractMeshCodes(string value)
    {
        return MeshCodeTokenRegex
            .Matches(value)
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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

    private static IEnumerable<string> EnumerateDetailedMeshCodes(string parentMeshCode)
    {
        for (int row = 0; row < 10; row++)
        {
            for (int column = 0; column < 10; column++)
            {
                yield return $"{parentMeshCode}{row}{column}";
            }
        }
    }

    private sealed record CandidateSourceFile(
        string AbsolutePath,
        string RelativePath,
        string PackageName,
        bool IsRequestedPackage,
        string[] FileMeshCodes,
        string[] DirectoryMeshCodes);

    private sealed class MeshCodeRequestMatcher
    {
        private readonly string[] exactCodes;
        private readonly Regex? regex;

        private MeshCodeRequestMatcher(string input, string[] exactCodes, Regex? regex)
        {
            Input = input;
            this.exactCodes = exactCodes;
            this.regex = regex;
        }

        public string Input { get; }

        public bool IsLiteral => regex is null;

        public static MeshCodeRequestMatcher CreateExact(string input, string[] exactCodes)
        {
            return new MeshCodeRequestMatcher(input, exactCodes, regex: null);
        }

        public static MeshCodeRequestMatcher CreateRegex(string input, Regex regex)
        {
            return new MeshCodeRequestMatcher(input, [], regex);
        }

        public IEnumerable<string> GetRequestedMeshCodes(string candidateMeshCode)
        {
            if (IsLiteral)
            {
                if (exactCodes.Contains(candidateMeshCode, StringComparer.Ordinal))
                {
                    yield return candidateMeshCode;
                }

                yield break;
            }

            if (regex!.IsMatch(candidateMeshCode))
            {
                yield return candidateMeshCode;
                yield break;
            }

            if (candidateMeshCode.Length != 6)
            {
                yield break;
            }

            foreach (string detailedMeshCode in EnumerateDetailedMeshCodes(candidateMeshCode))
            {
                if (regex.IsMatch(detailedMeshCode))
                {
                    yield return detailedMeshCode;
                }
            }
        }

        public string? Match(IEnumerable<string> candidates, IReadOnlySet<string> requestedMeshCodes)
        {
            string[] candidateArray = candidates
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (regex is null)
            {
                return exactCodes
                    .OrderByDescending(static code => code.Length)
                    .FirstOrDefault(code => candidateArray.Contains(code, StringComparer.Ordinal));
            }

            return candidateArray
                .Where(requestedMeshCodes.Contains)
                .OrderByDescending(static candidate => candidate.Length)
                .ThenBy(static candidate => candidate, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public bool RequiresMeshAreaFilter(string matchedMeshCode)
        {
            return IsLiteral
                && exactCodes.Length > 0
                && matchedMeshCode.Length < exactCodes[0].Length;
        }
    }
}

public sealed record LocalCityGmlSourceFileDescriptor(
    string AbsolutePath,
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter);

public sealed record LocalCityGmlSourceFileDiscoveryResult(
    IReadOnlyList<LocalCityGmlSourceFileDescriptor> SourceFiles,
    IReadOnlyList<string> RequestedMeshCodes);
