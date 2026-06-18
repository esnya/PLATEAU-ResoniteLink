using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Plateau.Application.Importing.CityGml;

using PlateauResoniteLink.Core.Application.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing;

public sealed class DatasetInspectionService
{
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;

    internal DatasetInspectionService(IPlateauDatasetContentSourceFactory datasetContentSourceFactory)
    {
        this.datasetContentSourceFactory = datasetContentSourceFactory ?? throw new ArgumentNullException(nameof(datasetContentSourceFactory));
    }

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
            IPlateauDatasetContentSource datasetSource = await datasetContentSourceFactory.CreateAsync(sourcePath, cancellationToken);
            LocalCityGmlSourceFileDiscoveryResult discovery = LocalCityGmlSourceFileDiscovery.Discover(
                datasetSource.EnumerateFiles(),
                meshCode,
                packageNames);

            DatasetSearchEntry[] entries = discovery.SourceFiles
                .Select(static descriptor => new DatasetSearchEntry(
                    descriptor.RelativePath,
                    descriptor.PackageName,
                    descriptor.MatchedMeshCode,
                    descriptor.RequiresMeshCodeBoundsFilter))
                .ToArray();

            return new DatasetSearchResult(
                entries,
                discovery.SelectedMeshCodes.ToArray());
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
        try
        {
            IPlateauDatasetContentSource datasetSource = await datasetContentSourceFactory.CreateAsync(sourcePath, cancellationToken);
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
            Dictionary<string, HashSet<string>> textureReferencesByPackage = new(StringComparer.Ordinal);
            Dictionary<string, GeometryVramAccumulator> geometryByPackage = new(StringComparer.Ordinal);

            foreach (LocalCityGmlDatasetSourceFileCandidate candidate in candidates)
            {
                await using Stream stream = await datasetSource.OpenReadAsync(candidate.RelativePath, cancellationToken);
                DatasetSourceFileStats sourceFileStats = await DatasetSourceFileStatsReader.ReadAsync(
                    stream,
                    candidate.RelativePath,
                    candidate.PackageName,
                    datasetSource,
                    cancellationToken);

                if (sourceFileStats.LodLevels.Count == 0)
                {
                    filesWithoutDetectedLod++;
                }

                foreach (int lodLevel in sourceFileStats.LodLevels)
                {
                    lodCounts.TryGetValue(lodLevel, out int currentCount);
                    lodCounts[lodLevel] = currentCount + 1;
                }

                if (sourceFileStats.ResolvedTexturePaths.Count > 0)
                {
                    if (!textureReferencesByPackage.TryGetValue(candidate.PackageName, out HashSet<string>? textureReferences))
                    {
                        textureReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        textureReferencesByPackage[candidate.PackageName] = textureReferences;
                    }

                    foreach (string texturePath in sourceFileStats.ResolvedTexturePaths)
                    {
                        textureReferences.Add(texturePath);
                    }
                }

                GeometryVramAccumulator geometryAccumulator = DatasetGeometryVramEstimator.GetOrCreateAccumulator(
                    geometryByPackage,
                    candidate.PackageName);
                geometryAccumulator.PositionCount += sourceFileStats.Geometry.PositionCount;
                geometryAccumulator.TriangleCount += sourceFileStats.Geometry.TriangleCount;
            }

            DatasetTextureVramEstimate textureVramEstimate = await DatasetTextureVramEstimator.EstimateAsync(
                datasetSource,
                textureReferencesByPackage,
                cancellationToken);

            DatasetGeometryVramEstimate geometryVramEstimate = DatasetGeometryVramEstimator.CreateEstimate(geometryByPackage);

            return new DatasetStatsResult(
                candidates.Length,
                packageCounts,
                meshCodeCounts,
                lodCounts.OrderBy(static pair => pair.Key).ToDictionary(static pair => pair.Key, static pair => pair.Value),
                filesWithoutDetectedLod,
                new DatasetArchiveVramEstimate(
                    textureVramEstimate,
                    geometryVramEstimate,
                    textureVramEstimate.RendererTotalBytes + geometryVramEstimate.RendererBytesMin,
                    textureVramEstimate.RendererTotalBytes + geometryVramEstimate.RendererBytesMax));
        }
        catch (ArgumentException exception)
        {
            throw new PlateauImportValidationException([exception.Message]);
        }
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
    IReadOnlyList<string> SelectedMeshCodes);

public sealed record DatasetSearchEntry(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshCodeBoundsFilter);

public sealed record DatasetStatsResult(
    int RecognizedSourceFileCount,
    IReadOnlyDictionary<string, int> PackageCounts,
    IReadOnlyDictionary<string, int> MeshCodeCounts,
    IReadOnlyDictionary<int, int> LodCoverageCounts,
    int FilesWithoutDetectedLod,
    DatasetArchiveVramEstimate ArchiveVramEstimate);

public sealed record DatasetArchiveVramEstimate(
    DatasetTextureVramEstimate RendererTextureVram,
    DatasetGeometryVramEstimate RendererGeometryVram,
    long RendererTotalBytesMin,
    long RendererTotalBytesMax);

public sealed record DatasetTextureVramEstimate(
    int ReferencedTextureCount,
    int ResolvedTextureFileCount,
    int MissingTextureReferenceCount,
    long Bc1TextureCount,
    long Bc3TextureCount,
    long Bc1Bytes,
    long Bc3Bytes,
    long RendererTotalBytes,
    long Rgba32PayloadBytes,
    IReadOnlyDictionary<string, DatasetPackageTextureVramEstimate> PackageEstimates);

public sealed record DatasetPackageTextureVramEstimate(
    int ResolvedTextureFileCount,
    int MissingTextureReferenceCount,
    long Bc1TextureCount,
    long Bc3TextureCount,
    long Bc1Bytes,
    long Bc3Bytes,
    long RendererTotalBytes,
    long Rgba32PayloadBytes);

public sealed record DatasetGeometryVramEstimate(
    long PositionCount,
    long TriangleCount,
    long VertexBufferBytesMin,
    long VertexBufferBytesMax,
    long IndexBufferBytes,
    long RendererBytesMin,
    long RendererBytesMax,
    IReadOnlyDictionary<string, DatasetPackageGeometryVramEstimate> PackageEstimates);

public sealed record DatasetPackageGeometryVramEstimate(
    string PackageName,
    long PositionCount,
    long TriangleCount,
    long VertexBufferBytesMin,
    long VertexBufferBytesMax,
    long IndexBufferBytes,
    long RendererBytesMin,
    long RendererBytesMax);
