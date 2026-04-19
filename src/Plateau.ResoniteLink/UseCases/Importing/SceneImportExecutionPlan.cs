using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record SceneImportExecutionPlan
{
    public SceneImportExecutionPlan(
        PlateauImportRequest normalizedRequest,
        SceneBuildRequest sceneBuildRequest)
    {
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentNullException.ThrowIfNull(sceneBuildRequest);

        ValidateRequestConsistency(normalizedRequest, sceneBuildRequest.Metadata.Request);

        NormalizedRequest = normalizedRequest;
        SceneBuildRequest = sceneBuildRequest;
    }

    public PlateauImportRequest NormalizedRequest { get; }

    public SceneBuildRequest SceneBuildRequest { get; }

    public static SceneImportExecutionPlan Create(
        PlateauImportRequest normalizedRequest,
        ConstructionMetadata metadata,
        string resolvedSourcePath,
        string workRoot)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        return new SceneImportExecutionPlan(
            normalizedRequest,
            new SceneBuildRequest(
                metadata,
                resolvedSourcePath,
                workRoot));
    }

    private static void ValidateRequestConsistency(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest buildRequest)
    {
        if (!string.Equals(normalizedRequest.Dataset, buildRequest.Dataset, StringComparison.Ordinal)
            || !string.Equals(normalizedRequest.MeshCode, buildRequest.MeshCode, StringComparison.Ordinal)
            || !HasCompatibleSourceResolution(normalizedRequest, buildRequest)
            || normalizedRequest.IncludeMarkingAlways != buildRequest.IncludeMarkingAlways
            || normalizedRequest.DemTerrainMode != buildRequest.DemTerrainMode
            || normalizedRequest.DemHeightmapMetersPerVertex != buildRequest.DemHeightmapMetersPerVertex
            || normalizedRequest.DemHeightmapMaxResolution != buildRequest.DemHeightmapMaxResolution
            || !SequenceEqual(normalizedRequest.PackageNames, buildRequest.PackageNames)
            || !SetEqual(normalizedRequest.GlobalExcludeLodLevels, buildRequest.GlobalExcludeLodLevels)
            || !DictionaryOfSetsEqual(normalizedRequest.ExcludeLodLevelsByPackage, buildRequest.ExcludeLodLevelsByPackage)
            || !DictionaryEqual(normalizedRequest.PackagePatterns, buildRequest.PackagePatterns))
        {
            throw new ArgumentException(
                "Scene import execution plan requires normalized and build requests to match for execution identity and import options. Only source-resolved location values may differ.",
                nameof(buildRequest));
        }
    }

    private static bool HasCompatibleSourceResolution(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest buildRequest)
    {
        if (normalizedRequest.SourceKind == buildRequest.SourceKind)
        {
            return true;
        }

        return normalizedRequest.SourceKind == DatasetSourceKind.Remote
            && buildRequest.SourceKind == DatasetSourceKind.Local;
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
