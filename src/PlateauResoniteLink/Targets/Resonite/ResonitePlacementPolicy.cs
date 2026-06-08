using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResonitePlacementPolicy
{
    public static IReadOnlyDictionary<string, string> CreateSourceFileSlotNamesByRelativePath(
        IReadOnlyList<string> relativeSourceFiles)
    {
        Dictionary<string, string> slotNamesByPath = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> pathsByStem = new(StringComparer.Ordinal);

        foreach (string relativeSourceFile in relativeSourceFiles)
        {
            if (string.IsNullOrWhiteSpace(relativeSourceFile))
            {
                continue;
            }

            string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
            if (string.IsNullOrWhiteSpace(fileStem))
            {
                continue;
            }

            if (!pathsByStem.TryGetValue(fileStem, out List<string>? paths))
            {
                paths = [];
                pathsByStem.Add(fileStem, paths);
            }

            paths.Add(relativeSourceFile);
        }

        foreach ((string fileStem, List<string> paths) in pathsByStem)
        {
            paths.Sort(StringComparer.Ordinal);
            if (paths.Count == 1)
            {
                slotNamesByPath[paths[0]] = fileStem;
                continue;
            }

            foreach (string path in paths)
            {
                slotNamesByPath[path] = $"{fileStem}_{ComputeStableHashSuffix(path)}";
            }
        }

        return slotNamesByPath;
    }

    public static string ResolveSourceFileSlotName(
        ResoniteConstructionCityObject cityObject,
        string sourceFileRelativePath,
        IReadOnlyDictionary<string, string> sourceFileSlotNamesByRelativePath)
    {
        if (sourceFileSlotNamesByRelativePath.TryGetValue(sourceFileRelativePath, out string? slotName)
            && !string.IsNullOrWhiteSpace(slotName))
        {
            return slotName;
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            string fileStem = Path.GetFileNameWithoutExtension(cityObject.SourceFileRelativePath);
            if (!string.IsNullOrWhiteSpace(fileStem))
            {
                return fileStem;
            }
        }

        throw new InvalidOperationException(
            $"City object '{cityObject.DisplayName}' did not provide source-file metadata. "
            + "source-file-owned hierarchy requires SourceFileRelativePath.");
    }

    public static string ResolveSourceFileRelativePath(ResoniteConstructionCityObject cityObject)
    {
        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            return cityObject.SourceFileRelativePath!;
        }

        throw new InvalidOperationException(
            $"City object '{cityObject.DisplayName}' did not provide source-file metadata. "
            + "source-file-owned hierarchy requires SourceFileRelativePath.");
    }

    public static string ResolveRequiredSourceFileRootMeshCode(
        string? sourceFileRootMeshCode,
        string sourceFileSlotName,
        string actualMeshCode)
    {
        if (!string.IsNullOrWhiteSpace(sourceFileRootMeshCode)
            && PlateauMeshCode.TryGetGeodeticCenter(sourceFileRootMeshCode, out _))
        {
            return sourceFileRootMeshCode;
        }

        return ResolveRequiredSourceFileRootMeshCode(sourceFileSlotName, actualMeshCode);
    }

    public static string ResolveRequiredSourceFileRootMeshCode(string sourceFileSlotName, string actualMeshCode)
    {
        if (ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(sourceFileSlotName, out string meshCode))
        {
            return meshCode;
        }

        if (PlateauMeshCode.TryGetGeodeticCenter(actualMeshCode, out _))
        {
            return actualMeshCode;
        }

        throw new InvalidOperationException(
            $"source-file root '{sourceFileSlotName}' did not contain a concrete mesh-code and actual mesh-code '{actualMeshCode}' was not concrete.");
    }

    public static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    public static ResoniteFloat3 ResolveCityObjectLocalPosition(
        ResoniteLocalOrigin requestOrigin,
        string rootMeshCode,
        ResoniteFloat3? observedRootPosition,
        ResoniteFloat3 cityObjectPosition)
    {
        if (!PlateauMeshCode.TryGetGeodeticCenter(rootMeshCode, out GeodeticCoordinate rootMeshCenter))
        {
            return new ResoniteFloat3(
                cityObjectPosition.X,
                cityObjectPosition.Y - (observedRootPosition?.Y ?? 0.0),
                cityObjectPosition.Z);
        }

        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(
            new GeodeticCoordinate(requestOrigin.Latitude, requestOrigin.Longitude, requestOrigin.Altitude),
            rootMeshCenter);
        ResoniteFloat3 rootPosition = new(
            rootOffsetFromRequest.X,
            observedRootPosition?.Y ?? rootOffsetFromRequest.Y,
            rootOffsetFromRequest.Z);
        return Subtract(cityObjectPosition, rootPosition);
    }

    public static ResoniteFloat3 ResolveMeshRootPosition(
        ResoniteLocalOrigin requestOrigin,
        string rootMeshCode,
        double? observedRootHeight = null)
    {
        if (!PlateauMeshCode.TryGetGeodeticCenter(rootMeshCode, out GeodeticCoordinate rootMeshCenter))
        {
            return new ResoniteFloat3(0.0, observedRootHeight ?? 0.0, 0.0);
        }

        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(
            new GeodeticCoordinate(requestOrigin.Latitude, requestOrigin.Longitude, requestOrigin.Altitude),
            rootMeshCenter);
        return new ResoniteFloat3(
            rootOffsetFromRequest.X,
            observedRootHeight ?? rootOffsetFromRequest.Y,
            rootOffsetFromRequest.Z);
    }

    public static ResonitePlacementCorrectionResult EvaluateRootPlacementCorrection(
        ResoniteLocalOrigin requestOrigin,
        string rootMeshCode,
        double? observedRootHeight = null)
    {
        List<ResonitePlacementCorrectionTerm> placementTerms = [];
        List<ResonitePlacementCorrectionTerm> postPlacementTerms = [];

        if (!PlateauMeshCode.TryGetGeodeticCenter(rootMeshCode, out GeodeticCoordinate rootMeshCenter))
        {
            if (observedRootHeight.HasValue)
            {
                postPlacementTerms.Add(new ResonitePlacementCorrectionTerm(
                    ResoniteCorrectionAxis.Y,
                    observedRootHeight.Value,
                    ResonitePlacementCorrectionReason.ObservedRootHeight));
            }

            return new ResonitePlacementCorrectionResult(
                new ResoniteFloat3(0.0, observedRootHeight ?? 0.0, 0.0),
                new ResonitePlacementCorrectionLayers(
                    [],
                    [],
                    placementTerms,
                    postPlacementTerms));
        }

        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(
            new GeodeticCoordinate(requestOrigin.Latitude, requestOrigin.Longitude, requestOrigin.Altitude),
            rootMeshCenter);
        placementTerms.Add(new ResonitePlacementCorrectionTerm(
            ResoniteCorrectionAxis.X,
            rootOffsetFromRequest.X,
            ResonitePlacementCorrectionReason.RequestRelativeMeshCodeOffset));
        placementTerms.Add(new ResonitePlacementCorrectionTerm(
            ResoniteCorrectionAxis.Z,
            rootOffsetFromRequest.Z,
            ResonitePlacementCorrectionReason.RequestRelativeMeshCodeOffset));

        if (observedRootHeight.HasValue)
        {
            postPlacementTerms.Add(new ResonitePlacementCorrectionTerm(
                ResoniteCorrectionAxis.Y,
                observedRootHeight.Value,
                ResonitePlacementCorrectionReason.ObservedRootHeight));
        }

        return new ResonitePlacementCorrectionResult(
            new ResoniteFloat3(
            rootOffsetFromRequest.X,
            observedRootHeight ?? rootOffsetFromRequest.Y,
            rootOffsetFromRequest.Z),
            new ResonitePlacementCorrectionLayers(
                [],
                [],
                placementTerms,
                postPlacementTerms));
    }

    public static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    public static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    public static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        if (!PlateauMeshCode.TryGetGeodeticCenter(referenceMeshCode, out GeodeticCoordinate referenceCenter)
            || !PlateauMeshCode.TryGetGeodeticCenter(meshCode, out GeodeticCoordinate currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
    }

    public static ResoniteFloat3 ComputeOriginOffset(ResoniteLocalOrigin referenceCenter, ResoniteLocalOrigin currentCenter)
    {
        return ComputeOriginOffset(
            new GeodeticCoordinate(referenceCenter.Latitude, referenceCenter.Longitude, referenceCenter.Altitude),
            new GeodeticCoordinate(currentCenter.Latitude, currentCenter.Longitude, currentCenter.Altitude));
    }

    public static ResoniteFloat3 ComputeOriginOffset(GeodeticCoordinate referenceCenter, GeodeticCoordinate currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(X: eun.x, Y: 0.0, Z: eun.y);
    }

    private static string ComputeStableHashSuffix(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
