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

        ValidateNormalizedAndResolvedRequestConsistency(normalizedRequest, resolvedRequest);
        ValidateResolvedRemoteResourceLocations(normalizedRequest, resolvedRequest, sceneBuildRequest.WorkRoot);
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
        PlateauImportRequest resolvedRequest)
    {
        if (!string.Equals(normalizedRequest.Dataset, resolvedRequest.Dataset, StringComparison.Ordinal)
            || !string.Equals(normalizedRequest.MeshCode, resolvedRequest.MeshCode, StringComparison.Ordinal)
            || !HasCompatibleSourceResolution(normalizedRequest.Source, resolvedRequest.Source)
            || !HasCompatibleSourceResolution(normalizedRequest.DemTextureSource, resolvedRequest.DemTextureSource)
            || normalizedRequest.IncludeMarkingAlways != resolvedRequest.IncludeMarkingAlways
            || normalizedRequest.DemTerrainMode != resolvedRequest.DemTerrainMode
            || normalizedRequest.DemHeightmapMetersPerVertex != resolvedRequest.DemHeightmapMetersPerVertex
            || normalizedRequest.DemHeightmapMaxResolution != resolvedRequest.DemHeightmapMaxResolution
            || !SequenceEqual(normalizedRequest.PackageNames, resolvedRequest.PackageNames)
            || !SetEqual(normalizedRequest.GlobalExcludeLodLevels, resolvedRequest.GlobalExcludeLodLevels)
            || !DictionaryOfSetsEqual(normalizedRequest.ExcludeLodLevelsByPackage, resolvedRequest.ExcludeLodLevelsByPackage)
            || !DictionaryEqual(normalizedRequest.PackagePatterns, resolvedRequest.PackagePatterns))
        {
            throw new ArgumentException(
                "Scene import execution plan requires normalized and resolved requests to preserve execution identity and import options. Only source resolution from remote to local is allowed.",
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
        PlateauImportSource? normalizedSource,
        PlateauImportSource? resolvedSource)
    {
        if (Equals(normalizedSource, resolvedSource))
        {
            return true;
        }

        return normalizedSource?.SourceKind == DatasetSourceKind.Remote
            && resolvedSource?.SourceKind == DatasetSourceKind.Local;
    }

    private static void ValidateResolvedRemoteResourceLocations(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        string workRoot)
    {
        ValidateResolvedRemoteResourceLocation(
            normalizedRequest.Source,
            resolvedRequest.Source,
            workRoot,
            "source-archive");
        ValidateResolvedRemoteResourceLocation(
            normalizedRequest.DemTextureSource,
            resolvedRequest.DemTextureSource,
            workRoot,
            "source-ortho");
    }

    private static void ValidateResolvedRemoteResourceLocation(
        PlateauImportSource? normalizedSource,
        PlateauImportSource? resolvedSource,
        string workRoot,
        string remoteResourcePrefix)
    {
        if (normalizedSource is not PlateauRemoteImportSource remoteSource
            || resolvedSource is not PlateauLocalImportSource localSource)
        {
            return;
        }

        if (!WorkRootLayout.MatchesRemoteResourcePath(
                localSource.LocalSourcePath!,
                workRoot,
                remoteSource.ServerUri!,
                remoteResourcePrefix))
        {
            throw new ArgumentException(
                $"Scene import execution plan requires resolved local path '{localSource.LocalSourcePath}' to match the deterministic materialization path for remote source '{remoteSource.ServerUri}'.",
                nameof(resolvedSource));
        }
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
