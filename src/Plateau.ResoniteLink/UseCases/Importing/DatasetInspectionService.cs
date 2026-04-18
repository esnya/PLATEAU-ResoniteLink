using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class DatasetInspectionService
{
    private static readonly Regex LodTokenRegex = new(
        @"lod(?<lod>\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The inspection API stays instance-based so the CLI can depend on a single extensible service contract.")]
    public async Task<DatasetSearchResult> SearchAsync(
        string sourcePath,
        string meshCode,
        IReadOnlyList<string>? packageNames,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(sourcePath, cancellationToken);
            LocalCityGmlSourceFileDiscoveryResult discovery = LocalCityGmlSourceFileDiscovery.Discover(
                datasetSource.EnumerateFiles(),
                meshCode,
                packageNames);

            DatasetSearchEntry[] entries = discovery.SourceFiles
                .Select(static descriptor => new DatasetSearchEntry(
                    descriptor.RelativePath,
                    descriptor.PackageName,
                    descriptor.MatchedMeshCode,
                    descriptor.RequiresMeshAreaFilter))
                .ToArray();

            return new DatasetSearchResult(
                entries,
                discovery.RequestedMeshCodes.ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new PlateauImportValidationException([exception.Message]);
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The inspection API stays instance-based so the CLI can depend on a single extensible service contract.")]
    public async Task<DatasetStatsResult> GetStatsAsync(
        string sourcePath,
        IReadOnlyList<string>? packageNames,
        CancellationToken cancellationToken = default)
    {
        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(sourcePath, cancellationToken);
        LocalCityGmlDatasetSourceFileCandidate[] candidates = LocalCityGmlSourceFileDiscovery
            .EnumerateCandidates(datasetSource.EnumerateFiles(), packageNames)
            .Where(static candidate => candidate.IsRequestedPackage)
            .ToArray();

        Dictionary<string, int> packageCounts = candidates
            .GroupBy(static candidate => candidate.PackageName, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

        Dictionary<string, int> meshCodeCounts = candidates
            .Select(static candidate => ResolveRepresentativeMeshCode(candidate))
            .Where(static meshCode => meshCode is not null)
            .Select(static meshCode => meshCode!)
            .GroupBy(static meshCode => meshCode, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

        Dictionary<int, int> lodCounts = [];
        int filesWithoutDetectedLod = 0;

        foreach (LocalCityGmlDatasetSourceFileCandidate candidate in candidates)
        {
            await using Stream stream = await datasetSource.OpenReadAsync(candidate.RelativePath, cancellationToken);
            using StreamReader reader = new(stream);
            string content = await reader.ReadToEndAsync(cancellationToken);

            int[] lodLevels = LodTokenRegex.Matches(content)
                .Select(static match => int.Parse(match.Groups["lod"].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Distinct()
                .OrderBy(static lod => lod)
                .ToArray();

            if (lodLevels.Length == 0)
            {
                filesWithoutDetectedLod++;
                continue;
            }

            foreach (int lodLevel in lodLevels)
            {
                lodCounts.TryGetValue(lodLevel, out int currentCount);
                lodCounts[lodLevel] = currentCount + 1;
            }
        }

        return new DatasetStatsResult(
            candidates.Length,
            packageCounts,
            meshCodeCounts,
            lodCounts.OrderBy(static pair => pair.Key).ToDictionary(static pair => pair.Key, static pair => pair.Value),
            filesWithoutDetectedLod);
    }

    private static string? ResolveRepresentativeMeshCode(LocalCityGmlDatasetSourceFileCandidate candidate)
    {
        return candidate.FileMeshCodes
            .Concat(candidate.DirectoryMeshCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static meshCode => meshCode.Length)
            .ThenBy(static meshCode => meshCode, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

public sealed record DatasetSearchResult(
    IReadOnlyList<DatasetSearchEntry> SourceFiles,
    IReadOnlyList<string> RequestedMeshCodes);

public sealed record DatasetSearchEntry(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter);

public sealed record DatasetStatsResult(
    int RecognizedSourceFileCount,
    IReadOnlyDictionary<string, int> PackageCounts,
    IReadOnlyDictionary<string, int> MeshCodeCounts,
    IReadOnlyDictionary<int, int> LodCoverageCounts,
    int FilesWithoutDetectedLod);
