using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemCityObjectAggregation
{
    public static ParsedCityObject[] AggregateBySourceFileAndThirdMesh(
        SourceFileDescriptor sourceFile,
        IEnumerable<ParsedCityObject> cityObjects,
        IReadOnlyList<string>? selectedMeshCodes = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(cityObjects);

        ParsedCityObject[] objects = cityObjects.ToArray();
        if (!string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return objects;
        }

        return objects
            .SelectMany(cityObject => ResolveThirdMeshCodes(cityObject, sourceFile, selectedMeshCodes)
                .Select(thirdMeshCode => new DemCityObjectEntry(cityObject, thirdMeshCode)))
            .GroupBy(entry => new DemCityObjectKey(
                entry.CityObject.SourceFileRelativePath,
                entry.ThirdMeshCode),
                DemCityObjectKeyComparer.Instance)
            .OrderBy(static group => group.Key.SourceFileRelativePath, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.ThirdMeshCode, StringComparer.Ordinal)
            .Select(static group => AggregateGroup(group.Key, group.Select(static entry => entry.CityObject)))
            .Where(static cityObject => cityObject.Surfaces.Length > 0)
            .ToArray();
    }

    private static ParsedCityObject AggregateGroup(
        DemCityObjectKey key,
        IEnumerable<ParsedCityObject> cityObjects)
    {
        ParsedCityObject[] orderedCityObjects = cityObjects
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ThenBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();
        ParsedCityObject first = orderedCityObjects[0];
        ParsedSurface[] surfaces = orderedCityObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .OrderBy(static surface => surface, ParsedSurfaceStructuralComparer.Instance)
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

    private static string[] ResolveThirdMeshCodes(
        ParsedCityObject cityObject,
        SourceFileDescriptor sourceFile,
        IReadOnlyList<string>? selectedMeshCodes)
    {
        if (TryNormalizeThirdMeshCode(cityObject.ActualMeshCode, out string? actualMeshCode))
        {
            return [actualMeshCode!];
        }

        if (TryNormalizeThirdMeshCode(sourceFile.MatchedMeshCode, out string? matchedMeshCode))
        {
            return [matchedMeshCode!];
        }

        string[] selectedThirdMeshCodes = ResolveSelectedThirdMeshCodes(sourceFile, selectedMeshCodes);
        if (selectedThirdMeshCodes.Length > 0)
        {
            return selectedThirdMeshCodes;
        }

        return [cityObject.ActualMeshCode];
    }

    private static string[] ResolveSelectedThirdMeshCodes(
        SourceFileDescriptor sourceFile,
        IReadOnlyList<string>? selectedMeshCodes)
    {
        if (selectedMeshCodes is null
            || sourceFile.MatchedMeshCode.Length != 6)
        {
            return [];
        }

        return selectedMeshCodes
            .Where(meshCode => meshCode.Length >= 8
                && meshCode.StartsWith(sourceFile.MatchedMeshCode, StringComparison.Ordinal))
            .Select(static meshCode => meshCode[..8])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static meshCode => meshCode, StringComparer.Ordinal)
            .ToArray();
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

    private sealed record DemCityObjectEntry(
        ParsedCityObject CityObject,
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
