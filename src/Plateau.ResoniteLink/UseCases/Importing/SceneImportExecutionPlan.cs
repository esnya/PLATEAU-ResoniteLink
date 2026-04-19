using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record SceneImportExecutionPlan
{
    public SceneImportExecutionPlan(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        SceneBuildRequest sceneBuildRequest)
    {
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentNullException.ThrowIfNull(resolvedRequest);
        ArgumentNullException.ThrowIfNull(sceneBuildRequest);

        ValidateNormalizedAndResolvedRequestConsistency(normalizedRequest, resolvedRequest, sceneBuildRequest.DatasetContentSource);
        ValidateResolvedAndBuildRequestConsistency(resolvedRequest, sceneBuildRequest.Metadata.Request);

        NormalizedRequest = normalizedRequest;
        ResolvedRequest = resolvedRequest;
        SceneBuildRequest = sceneBuildRequest;
    }

    public PlateauImportRequest NormalizedRequest { get; }

    public PlateauImportRequest ResolvedRequest { get; }

    public SceneBuildRequest SceneBuildRequest { get; }

    public static SceneImportExecutionPlan Create(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        ConstructionMetadata metadata,
        IPlateauDatasetContentSource datasetContentSource,
        string workRoot)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(datasetContentSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        return new SceneImportExecutionPlan(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                metadata,
                datasetContentSource,
                workRoot));
    }

    private static void ValidateNormalizedAndResolvedRequestConsistency(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        IPlateauDatasetContentSource datasetContentSource)
    {
        List<string> mismatches = [];
        if (!string.Equals(normalizedRequest.Dataset, resolvedRequest.Dataset, StringComparison.Ordinal))
        {
            mismatches.Add("dataset");
        }

        if (!string.Equals(normalizedRequest.MeshCode, resolvedRequest.MeshCode, StringComparison.Ordinal))
        {
            mismatches.Add("mesh");
        }

        if (!HasCompatibleSourceResolution(normalizedRequest, resolvedRequest, datasetContentSource))
        {
            mismatches.Add("source");
        }

        if (!HasCompatibleDemTextureSourceResolution(normalizedRequest, resolvedRequest, datasetContentSource.SourcePath))
        {
            mismatches.Add("dem-source");
        }

        if (normalizedRequest.IncludeMarkingAlways != resolvedRequest.IncludeMarkingAlways)
        {
            mismatches.Add("include-marking");
        }

        if (normalizedRequest.DemTerrainMode != resolvedRequest.DemTerrainMode)
        {
            mismatches.Add("dem-mode");
        }

        if (normalizedRequest.DemHeightmapMetersPerVertex != resolvedRequest.DemHeightmapMetersPerVertex)
        {
            mismatches.Add("dem-meters-per-vertex");
        }

        if (normalizedRequest.DemHeightmapMaxResolution != resolvedRequest.DemHeightmapMaxResolution)
        {
            mismatches.Add("dem-max-resolution");
        }

        if (!SequenceEqual(normalizedRequest.PackageNames, resolvedRequest.PackageNames))
        {
            mismatches.Add("packages");
        }

        if (!SetEqual(normalizedRequest.GlobalExcludeLodLevels, resolvedRequest.GlobalExcludeLodLevels))
        {
            mismatches.Add("global-exclude-lods");
        }

        if (!DictionaryOfSetsEqual(normalizedRequest.ExcludeLodLevelsByPackage, resolvedRequest.ExcludeLodLevelsByPackage))
        {
            mismatches.Add("package-exclude-lods");
        }

        if (!DictionaryEqual(normalizedRequest.PackagePatterns, resolvedRequest.PackagePatterns))
        {
            mismatches.Add("package-patterns");
        }

        if (mismatches.Count > 0)
        {
            throw new ArgumentException(
                "Scene import execution plan requires normalized and resolved requests to preserve execution identity and import options. "
                + $"Mismatches: {string.Join(", ", mismatches)}.",
                nameof(resolvedRequest));
        }
    }

    private static void ValidateResolvedAndBuildRequestConsistency(
        PlateauImportRequest resolvedRequest,
        PlateauImportRequest buildRequest)
    {
        if (!Equals(resolvedRequest, buildRequest))
        {
            throw new ArgumentException(
                "Scene import execution plan requires resolved and build requests to match exactly.",
                nameof(buildRequest));
        }
    }

    private static bool HasCompatibleSourceResolution(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest buildRequest,
        IPlateauDatasetContentSource datasetContentSource)
    {
        if (Equals(normalizedRequest.Source, buildRequest.Source))
        {
            return true;
        }

        return normalizedRequest.Source is PlateauRemoteImportSource
            && buildRequest.Source is PlateauLocalImportSource localSource
            && string.Equals(localSource.LocalSourcePath, datasetContentSource.SourcePath, StringComparison.Ordinal);
    }

    private static bool HasCompatibleDemTextureSourceResolution(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest buildRequest,
        string datasetContentSourcePath)
    {
        if (Equals(normalizedRequest.DemTextureSource, buildRequest.DemTextureSource))
        {
            return true;
        }

        return normalizedRequest.DemTextureSource is PlateauRemoteImportSource { ServerUri: not null } remoteDemTextureSource
            && buildRequest.DemTextureSource is PlateauLocalImportSource localDemTextureSource
            && string.Equals(
                localDemTextureSource.LocalSourcePath,
                WorkRootLayout.GetRemoteResourcePath(datasetContentSourcePath, remoteDemTextureSource.ServerUri, "source-ortho"),
                StringComparison.Ordinal);
    }

    private static bool SequenceEqual(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static bool SetEqual(
        IReadOnlySet<int>? left,
        IReadOnlySet<int>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SetEquals(right);
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? rightValue)
                || !string.Equals(value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionaryOfSetsEqual(
        IReadOnlyDictionary<string, IReadOnlySet<int>>? left,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, IReadOnlySet<int> value) in left)
        {
            if (!right.TryGetValue(key, out IReadOnlySet<int>? rightValue)
                || !SetEqual(value, rightValue))
            {
                return false;
            }
        }

        return true;
    }
}
