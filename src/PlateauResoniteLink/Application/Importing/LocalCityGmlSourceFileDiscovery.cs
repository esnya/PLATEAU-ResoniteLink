using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlSourceFileDiscovery
{
    private static readonly Regex MeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8}|\d{6})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MeshCodeSegmentRegex = new(
        @"^(?:\d{8}|\d{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LocalCityGmlSourceFileDiscoveryResult Discover(
        IEnumerable<string> relativePaths,
        string meshCodeRequest,
        IReadOnlyList<string>? packageNames)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCodeRequest);

        MeshCodeSelectionMatcher matcher = CreateSelectionMatcher(meshCodeRequest);
        LocalCityGmlDatasetSourceFileCandidate[] candidates = EnumerateCandidates(relativePaths, packageNames).ToArray();

        return CreateSourceFileDiscoveryResult(candidates, matcher);
    }

    internal static IReadOnlyList<LocalCityGmlDatasetSourceFileCandidate> EnumerateCandidates(
        IEnumerable<string> relativePaths,
        IReadOnlyList<string>? packageNames)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);

        HashSet<string>? requestedPackageNames = packageNames is null
            ? null
            : new HashSet<string>(
                PlateauPackageCatalog.NormalizeRequestedPackageNames(packageNames),
                StringComparer.OrdinalIgnoreCase);

        return relativePaths
            .Where(path => path.EndsWith(".gml", StringComparison.OrdinalIgnoreCase))
            .Select(path => CreateCandidateSourceFileFromRelativePath(path, requestedPackageNames))
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .ToArray();
    }

    private static MeshCodeSelectionMatcher CreateSelectionMatcher(string meshCodeRequest)
    {
        if (PlateauMeshCode.TryGetBounds(meshCodeRequest, out _))
        {
            return meshCodeRequest.Length >= 8
                ? MeshCodeSelectionMatcher.CreateExact(meshCodeRequest, [meshCodeRequest, meshCodeRequest[..6]])
                : MeshCodeSelectionMatcher.CreateExact(meshCodeRequest, [meshCodeRequest]);
        }

        if (!MeshCodeRequestSyntax.TryCreateSelectionRegex(meshCodeRequest, out Regex? regex, out string? error))
        {
            throw new ArgumentException(error, nameof(meshCodeRequest));
        }

        return MeshCodeSelectionMatcher.CreateRegex(meshCodeRequest, regex!);
    }

    private static LocalCityGmlSourceFileDiscoveryResult CreateSourceFileDiscoveryResult(
        LocalCityGmlDatasetSourceFileCandidate[] candidates,
        MeshCodeSelectionMatcher matcher)
    {
        string[] selectedMeshCodes = candidates
            .Where(static candidate => candidate.IsRequestedPackage)
            .SelectMany(candidate => candidate.FileMeshCodes
                .Concat(candidate.DirectoryMeshCodes)
                .SelectMany(meshCode => matcher.GetSelectedMeshCodes(meshCode, candidate.PackageName)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static meshCode => meshCode, StringComparer.Ordinal)
            .ToArray();
        GeodeticCoordinate? requestedCenter = TryResolveRequestedCenter(matcher.RequestedValue, selectedMeshCodes);
        HashSet<string> requestedMeshCodeSet = selectedMeshCodes.ToHashSet(StringComparer.Ordinal);
        HashSet<string> parentMeshCodes = selectedMeshCodes
            .Where(static matchedMeshCode => matchedMeshCode.Length == 8)
            .Select(static matchedMeshCode => matchedMeshCode[..6])
            .ToHashSet(StringComparer.Ordinal);

        LocalCityGmlSourceFileDescriptor[] sourceFiles = candidates
            .Select(candidate => CreateSourceFileDescriptor(candidate, matcher, requestedMeshCodeSet, parentMeshCodes))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .OrderBy(descriptor => GetMeshCodeCenterDistanceSquared(descriptor, requestedCenter))
            .ThenBy(static descriptor => descriptor.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new LocalCityGmlSourceFileDiscoveryResult(sourceFiles, selectedMeshCodes);
    }

    private static LocalCityGmlDatasetSourceFileCandidate? CreateCandidateSourceFile(
        string datasetRoot,
        string path,
        HashSet<string>? requestedPackageNames)
    {
        string relativePath = NormalizePath(Path.GetRelativePath(datasetRoot, path));
        return CreateCandidateSourceFileCore(path, relativePath, requestedPackageNames);
    }

    private static LocalCityGmlDatasetSourceFileCandidate? CreateCandidateSourceFileFromRelativePath(
        string relativePath,
        HashSet<string>? requestedPackageNames)
    {
        string normalizedPath = NormalizePath(relativePath);
        return CreateCandidateSourceFileCore(normalizedPath, normalizedPath, requestedPackageNames);
    }

    private static LocalCityGmlDatasetSourceFileCandidate? CreateCandidateSourceFileCore(
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

        return new LocalCityGmlDatasetSourceFileCandidate(
            absolutePath,
            relativePath,
            packageName,
            requestedPackageNames is null || requestedPackageNames.Contains(packageName),
            fileMeshCodes,
            directoryMeshCodes);
    }

    private static LocalCityGmlSourceFileDescriptor? CreateSourceFileDescriptor(
        LocalCityGmlDatasetSourceFileCandidate candidate,
        MeshCodeSelectionMatcher matcher,
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
                matcher.RequiresMeshCodeBoundsFilter(matchedMeshCode)
                || RequiresParentDemMeshCodeBoundsFilter(candidate.PackageName, matchedMeshCode, requestedMeshCodes));
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
            RequiresMeshCodeBoundsFilter: true);
    }

    private static bool RequiresParentDemMeshCodeBoundsFilter(
        string packageName,
        string matchedMeshCode,
        IReadOnlySet<string> requestedMeshCodes)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
            && matchedMeshCode.Length == 6
            && requestedMeshCodes.Any(meshCode => meshCode.Length >= 8
                && meshCode.StartsWith(matchedMeshCode, StringComparison.Ordinal));
    }

    private static string[] ExtractMeshCodes(string value)
    {
        return MeshCodeTokenRegex
            .Matches(value)
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static GeodeticCoordinate? TryResolveRequestedCenter(
        string inputMeshCode,
        IReadOnlyList<string> requestedMeshCodes)
    {
        if (PlateauMeshCode.TryGetGeodeticCenter(inputMeshCode, out GeodeticCoordinate literalCenter))
        {
            return literalCenter;
        }

        List<(double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude)> bounds = [];
        foreach (string requestedMeshCode in requestedMeshCodes)
        {
            if (PlateauMeshCode.TryGetBounds(
                    requestedMeshCode,
                    out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) resolvedBounds))
            {
                bounds.Add(resolvedBounds);
            }
        }

        if (bounds.Count == 0)
        {
            return null;
        }

        double southLatitude = bounds.Min(static bound => bound.SouthLatitude);
        double northLatitude = bounds.Max(static bound => bound.NorthLatitude);
        double westLongitude = bounds.Min(static bound => bound.WestLongitude);
        double eastLongitude = bounds.Max(static bound => bound.EastLongitude);

        return new GeodeticCoordinate(
            Latitude: (southLatitude + northLatitude) / 2.0,
            Longitude: (westLongitude + eastLongitude) / 2.0,
            Altitude: 0.0);
    }

    private static double GetMeshCodeCenterDistanceSquared(
        LocalCityGmlSourceFileDescriptor descriptor,
        GeodeticCoordinate? requestedCenter)
    {
        if (requestedCenter is null
            || !PlateauMeshCode.TryGetGeodeticCenter(descriptor.MatchedMeshCode, out GeodeticCoordinate meshCenter))
        {
            return double.PositiveInfinity;
        }

        double latitudeDelta = meshCenter.Latitude - requestedCenter.Latitude;
        double longitudeDelta = meshCenter.Longitude - requestedCenter.Longitude;
        return (latitudeDelta * latitudeDelta) + (longitudeDelta * longitudeDelta);
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

    private sealed class MeshCodeSelectionMatcher
    {
        private readonly string[] exactCodes;
        private readonly Regex? regex;

        private MeshCodeSelectionMatcher(string requestedValue, string[] exactCodes, Regex? regex)
        {
            RequestedValue = requestedValue;
            this.exactCodes = exactCodes;
            this.regex = regex;
        }

        public string RequestedValue { get; }

        public bool IsLiteral => regex is null;

        public static MeshCodeSelectionMatcher CreateExact(string requestedValue, string[] exactCodes)
        {
            return new MeshCodeSelectionMatcher(requestedValue, exactCodes, regex: null);
        }

        public static MeshCodeSelectionMatcher CreateRegex(string requestedValue, Regex regex)
        {
            return new MeshCodeSelectionMatcher(requestedValue, [], regex);
        }

        public IEnumerable<string> GetSelectedMeshCodes(string candidateMeshCode, string packageName)
        {
            bool isDem = string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase);
            if (IsLiteral)
            {
                if (isDem
                    && RequestedValue.Length == 6
                    && candidateMeshCode.Length == 6
                    && exactCodes.Contains(candidateMeshCode, StringComparer.Ordinal))
                {
                    foreach (string detailedMeshCode in EnumerateDetailedMeshCodes(candidateMeshCode))
                    {
                        yield return detailedMeshCode;
                    }

                    yield break;
                }

                if (isDem
                    && RequestedValue.Length == 6
                    && candidateMeshCode.Length == 8
                    && candidateMeshCode.StartsWith(RequestedValue, StringComparison.Ordinal))
                {
                    yield return candidateMeshCode;
                    yield break;
                }

                if (candidateMeshCode.Length == RequestedValue.Length
                    && exactCodes.Contains(candidateMeshCode, StringComparer.Ordinal))
                {
                    yield return candidateMeshCode;
                }
                else if (RequestedValue.Length == 8
                    && candidateMeshCode.Length == 6
                    && string.Equals(candidateMeshCode, RequestedValue[..6], StringComparison.Ordinal))
                {
                    yield return RequestedValue;
                }

                yield break;
            }

            if (candidateMeshCode.Length == 6)
            {
                string[] detailedMeshCodes = EnumerateDetailedMeshCodes(candidateMeshCode)
                    .Where(meshCode => regex!.IsMatch(meshCode))
                    .ToArray();
                if (isDem && detailedMeshCodes.Length > 0)
                {
                    foreach (string detailedMeshCode in detailedMeshCodes)
                    {
                        yield return detailedMeshCode;
                    }

                    yield break;
                }

                if (regex!.IsMatch(candidateMeshCode))
                {
                    yield return candidateMeshCode;
                    yield break;
                }

                foreach (string detailedMeshCode in detailedMeshCodes)
                {
                    yield return detailedMeshCode;
                }

                yield break;
            }

            if (regex!.IsMatch(candidateMeshCode))
            {
                yield return candidateMeshCode;
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

        public bool RequiresMeshCodeBoundsFilter(string matchedMeshCode)
        {
            return IsLiteral
                && exactCodes.Length > 0
                && matchedMeshCode.Length < exactCodes[0].Length;
        }
    }
}

internal sealed record LocalCityGmlDatasetSourceFileCandidate(
    string AbsolutePath,
    string RelativePath,
    string PackageName,
    bool IsRequestedPackage,
    string[] FileMeshCodes,
    string[] DirectoryMeshCodes);

internal sealed record LocalCityGmlSourceFileDescriptor(
    string AbsolutePath,
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshCodeBoundsFilter);

internal sealed record LocalCityGmlSourceFileDiscoveryResult(
    IReadOnlyList<LocalCityGmlSourceFileDescriptor> SourceFiles,
    IReadOnlyList<string> SelectedMeshCodes);
