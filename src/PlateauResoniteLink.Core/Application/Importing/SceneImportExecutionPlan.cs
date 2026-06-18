using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Core.Application.Importing;

public sealed record SceneImportExecutionPlan
{
    private SceneImportExecutionPlan(
        SceneImportRequest sceneImportRequest)
    {
        ArgumentNullException.ThrowIfNull(sceneImportRequest);

        SceneImportRequest = sceneImportRequest;
    }

    public SceneImportRequest SceneImportRequest { get; }

    public static SceneImportExecutionPlan Create(
        ResolvedLocalPlateauImportRequest resolvedRequest,
        ImportedSceneMetadata metadata,
        string workRoot,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(resolvedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        PlateauImportRequest resolvedImportRequest = resolvedRequest.ToImportRequest();
        ValidateMetadataRequestConsistency(resolvedImportRequest, metadata.Request);

        return new SceneImportExecutionPlan(
            new SceneImportRequest(
                metadata,
                workRoot,
                commonMaterials));
    }

    private static void ValidateMetadataRequestConsistency(
        PlateauImportRequest expectedRequest,
        PlateauImportRequest metadataRequest)
    {
        if (!string.Equals(expectedRequest.Dataset, metadataRequest.Dataset, StringComparison.Ordinal)
            || !string.Equals(expectedRequest.MeshCode, metadataRequest.MeshCode, StringComparison.Ordinal)
            || !SourcesEqual(expectedRequest.CityGmlSource, metadataRequest.CityGmlSource)
            || !SourcesEqual(expectedRequest.DemTextureSource, metadataRequest.DemTextureSource)
            || expectedRequest.IncludeMarkingAlways != metadataRequest.IncludeMarkingAlways
            || expectedRequest.TerrainMeshMode != metadataRequest.TerrainMeshMode
            || expectedRequest.TerrainGridMetersPerVertex != metadataRequest.TerrainGridMetersPerVertex
            || expectedRequest.TerrainGridMaxResolution != metadataRequest.TerrainGridMaxResolution
            || expectedRequest.ExcludeGsiTerrainTiles != metadataRequest.ExcludeGsiTerrainTiles
            || !SequenceEqual(expectedRequest.PackageNames, metadataRequest.PackageNames)
            || !SetEqual(expectedRequest.GlobalExcludeLodLevels, metadataRequest.GlobalExcludeLodLevels)
            || !DictionaryOfSetsEqual(expectedRequest.ExcludeLodLevelsByPackage, metadataRequest.ExcludeLodLevelsByPackage)
            || !DictionaryEqual(expectedRequest.PackagePatterns, metadataRequest.PackagePatterns))
        {
            throw new ArgumentException(
                "Scene import execution plan requires imported scene metadata to carry the resolved local import request.",
                nameof(metadataRequest));
        }
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
