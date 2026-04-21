using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

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

        ValidateRequestConsistency(normalizedRequest, resolvedRequest, sceneBuildRequest.Metadata.Request, sceneBuildRequest.WorkRoot);

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
        ImportedSceneMetadata metadata,
        string resolvedSourcePath,
        string workRoot,
        IReadOnlyList<ResoniteMaterialBinding>? commonMaterials = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        return new SceneImportExecutionPlan(
            normalizedRequest,
            resolvedRequest,
            new SceneBuildRequest(
                metadata,
                resolvedSourcePath,
                workRoot,
                commonMaterials));
    }

    private static void ValidateRequestConsistency(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        PlateauImportRequest buildRequest,
        string workRoot)
    {
        if (!string.Equals(normalizedRequest.Dataset, buildRequest.Dataset, StringComparison.Ordinal)
            || !string.Equals(normalizedRequest.MeshCode, buildRequest.MeshCode, StringComparison.Ordinal)
            || !HasCompatibleSourceResolution(normalizedRequest.Source, resolvedRequest.Source, workRoot, "source-archive")
            || !HasCompatibleSourceResolution(normalizedRequest.DemTextureSource, resolvedRequest.DemTextureSource, workRoot, "source-ortho")
            || !SourcesEqual(resolvedRequest.Source, buildRequest.Source)
            || !SourcesEqual(resolvedRequest.DemTextureSource, buildRequest.DemTextureSource)
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
        PlateauImportSource? normalizedSource,
        PlateauImportSource? resolvedSource,
        string workRoot,
        string remotePathPrefix)
    {
        if (normalizedSource is null || resolvedSource is null)
        {
            return normalizedSource is null && resolvedSource is null;
        }

        if (normalizedSource.SourceKind == resolvedSource.SourceKind)
        {
            return SourcesEqual(normalizedSource, resolvedSource);
        }

        if (normalizedSource is PlateauRemoteImportSource remoteSource
            && resolvedSource is PlateauLocalImportSource localSource)
        {
            return RemoteDatasetResourceLayout.MatchesRemoteResourcePath(
                workRoot,
                remoteSource.ServerUri!,
                remotePathPrefix,
                localSource.LocalSourcePath);
        }

        return false;
    }

    private static bool SourcesEqual(PlateauImportSource? left, PlateauImportSource? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left switch
        {
            PlateauLocalImportSource leftLocal when right is PlateauLocalImportSource rightLocal =>
                string.Equals(leftLocal.LocalSourcePath, rightLocal.LocalSourcePath, StringComparison.Ordinal),
            PlateauRemoteImportSource leftRemote when right is PlateauRemoteImportSource rightRemote =>
                Equals(leftRemote.ServerUri, rightRemote.ServerUri),
            _ => false,
        };
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
