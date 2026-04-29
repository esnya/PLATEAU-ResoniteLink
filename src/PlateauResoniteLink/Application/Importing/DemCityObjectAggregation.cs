using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemCityObjectAggregation
{
    public static BootstrapParsedCityObject[] AggregateBySourceFileAndThirdMesh(
        SourceFileDescriptor sourceFile,
        IEnumerable<BootstrapParsedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(cityObjects);

        BootstrapParsedCityObject[] objects = cityObjects.ToArray();
        if (!string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return objects;
        }

        return objects
            .GroupBy(cityObject => new DemCityObjectKey(
                cityObject.SourceFileRelativePath,
                ResolveThirdMeshCode(cityObject, sourceFile)),
                DemCityObjectKeyComparer.Instance)
            .OrderBy(static group => group.Key.SourceFileRelativePath, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.ThirdMeshCode, StringComparer.Ordinal)
            .Select(static group => AggregateGroup(group.Key, group))
            .Where(static cityObject => cityObject.Surfaces.Length > 0)
            .ToArray();
    }

    private static BootstrapParsedCityObject AggregateGroup(
        DemCityObjectKey key,
        IEnumerable<BootstrapParsedCityObject> cityObjects)
    {
        BootstrapParsedCityObject[] orderedCityObjects = cityObjects
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ThenBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();
        BootstrapParsedCityObject first = orderedCityObjects[0];
        BootstrapParsedSurface[] surfaces = orderedCityObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .OrderBy(static surface => surface.PolygonId, StringComparer.Ordinal)
            .ThenBy(static surface => surface.ExteriorRing.RingId, StringComparer.Ordinal)
            .ToArray();

        return first with
        {
            SlotKey = CreateSlotKey(first.PackageName, key.SourceFileRelativePath, key.ThirdMeshCode),
            DisplayName = $"DEM {key.ThirdMeshCode}",
            ActualMeshCode = key.ThirdMeshCode,
            LodLevel = orderedCityObjects
                .Select(static cityObject => cityObject.LodLevel)
                .Where(static lodLevel => lodLevel.HasValue)
                .DefaultIfEmpty()
                .Max(),
            Surfaces = surfaces,
            SourceFileRelativePath = key.SourceFileRelativePath,
            SharedAcrossMeshCodes = orderedCityObjects.Any(static cityObject => cityObject.SharedAcrossMeshCodes),
            TerrainAligned = orderedCityObjects.Any(static cityObject => cityObject.TerrainAligned),
            GeodeticOriginOverride = null,
            FloorsAboveGround = null,
            MeasuredHeightMeters = null,
        };
    }

    private static string ResolveThirdMeshCode(
        BootstrapParsedCityObject cityObject,
        SourceFileDescriptor sourceFile)
    {
        if (TryNormalizeThirdMeshCode(cityObject.ActualMeshCode, out string? actualMeshCode))
        {
            return actualMeshCode!;
        }

        if (TryNormalizeThirdMeshCode(sourceFile.MatchedMeshCode, out string? matchedMeshCode))
        {
            return matchedMeshCode!;
        }

        return cityObject.ActualMeshCode;
    }

    private static bool TryNormalizeThirdMeshCode(string value, out string? meshCode)
    {
        meshCode = null;
        if (value.Length < 8)
        {
            return false;
        }

        string candidate = value[..8];
        if (!candidate.All(static character => character is >= '0' and <= '9'))
        {
            return false;
        }

        meshCode = candidate;
        return true;
    }

    private static string CreateSlotKey(
        string packageName,
        string sourceFileRelativePath,
        string thirdMeshCode)
    {
        string fileStem = Path.GetFileNameWithoutExtension(sourceFileRelativePath);
        return SanitizeIdentifier($"{packageName}_{fileStem}_{thirdMeshCode}");
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Concat(value.Select(static character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private sealed record DemCityObjectKey(
        string SourceFileRelativePath,
        string ThirdMeshCode);

    private sealed class DemCityObjectKeyComparer : IEqualityComparer<DemCityObjectKey>
    {
        public static readonly DemCityObjectKeyComparer Instance = new();

        public bool Equals(DemCityObjectKey? left, DemCityObjectKey? right)
        {
            return left is not null
                && right is not null
                && string.Equals(left.SourceFileRelativePath, right.SourceFileRelativePath, StringComparison.Ordinal)
                && string.Equals(left.ThirdMeshCode, right.ThirdMeshCode, StringComparison.Ordinal);
        }

        public int GetHashCode(DemCityObjectKey key)
        {
            HashCode hash = new();
            hash.Add(key.SourceFileRelativePath, StringComparer.Ordinal);
            hash.Add(key.ThirdMeshCode, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
