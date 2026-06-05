using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record SceneImportExecutionPlan
{
    public SceneImportExecutionPlan(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        SceneImportRequest sceneImportRequest)
    {
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentNullException.ThrowIfNull(resolvedRequest);
        ArgumentNullException.ThrowIfNull(sceneImportRequest);

        ValidateRequestConsistency(normalizedRequest, resolvedRequest, sceneImportRequest.Metadata.Request, sceneImportRequest.WorkRoot);

        NormalizedRequest = normalizedRequest;
        ResolvedRequest = resolvedRequest;
        SceneImportRequest = sceneImportRequest;
    }

    public PlateauImportRequest NormalizedRequest { get; }

    public PlateauImportRequest ResolvedRequest { get; }

    public SceneImportRequest SceneImportRequest { get; }

    public static SceneImportExecutionPlan Create(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        ImportedSceneMetadata metadata,
        string resolvedSourcePath,
        string workRoot,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedSourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        return new SceneImportExecutionPlan(
            normalizedRequest,
            resolvedRequest,
            new SceneImportRequest(
                metadata,
                resolvedSourcePath,
                workRoot,
                commonMaterials));
    }

    private static void ValidateRequestConsistency(
        PlateauImportRequest normalizedRequest,
        PlateauImportRequest resolvedRequest,
        PlateauImportRequest importRequest,
        string workRoot)
    {
        if (!string.Equals(normalizedRequest.Dataset, importRequest.Dataset, StringComparison.Ordinal)
            || !string.Equals(normalizedRequest.MeshCode, importRequest.MeshCode, StringComparison.Ordinal)
            || !HasCompatibleSourceResolution(normalizedRequest.CityGmlSource, resolvedRequest.CityGmlSource, workRoot, "source-archive")
            || !HasCompatibleSourceResolution(normalizedRequest.DemTextureSource, resolvedRequest.DemTextureSource, workRoot, "source-ortho")
            || !SourcesEqual(resolvedRequest.CityGmlSource, importRequest.CityGmlSource)
            || !SourcesEqual(resolvedRequest.DemTextureSource, importRequest.DemTextureSource)
            || normalizedRequest.IncludeMarkingAlways != importRequest.IncludeMarkingAlways
            || normalizedRequest.TerrainMeshMode != importRequest.TerrainMeshMode
            || normalizedRequest.TerrainGridMetersPerVertex != importRequest.TerrainGridMetersPerVertex
            || normalizedRequest.TerrainGridMaxResolution != importRequest.TerrainGridMaxResolution
            || !SequenceEqual(normalizedRequest.PackageNames, importRequest.PackageNames)
            || !SetEqual(normalizedRequest.GlobalExcludeLodLevels, importRequest.GlobalExcludeLodLevels)
            || !DictionaryOfSetsEqual(normalizedRequest.ExcludeLodLevelsByPackage, importRequest.ExcludeLodLevelsByPackage)
            || !DictionaryEqual(normalizedRequest.PackagePatterns, importRequest.PackagePatterns))
        {
            throw new ArgumentException(
                "Scene import execution plan requires normalized and import requests to match for execution identity and import options. Only source-resolved location values may differ.",
                nameof(importRequest));
        }
    }

    private static bool HasCompatibleSourceResolution(
        DatasetLocation? normalizedSource,
        DatasetLocation? resolvedSource,
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

        if (normalizedSource is RemoteDatasetLocation remoteSource
            && resolvedSource is LocalDatasetLocation localSource)
        {
            return RemoteDatasetResourceLayout.MatchesRemoteResourcePath(
                workRoot,
                remoteSource.ServerUri!,
                remotePathPrefix,
                localSource.LocalSourcePath);
        }

        return false;
    }

    private static bool SourcesEqual(DatasetLocation? left, DatasetLocation? right)
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
            LocalDatasetLocation leftLocal when right is LocalDatasetLocation rightLocal =>
                string.Equals(leftLocal.LocalSourcePath, rightLocal.LocalSourcePath, StringComparison.Ordinal),
            RemoteDatasetLocation leftRemote when right is RemoteDatasetLocation rightRemote =>
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
