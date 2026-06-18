using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using PlateauResoniteLink.Core;
using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResonitePlacementPolicy
{
    public static IReadOnlyDictionary<string, string> CreateSourceFileSlotNamesByRelativePath(
        IReadOnlyList<string> relativeSourceFiles,
        IReadOnlyDictionary<string, string> packageNamesByRelativePath)
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
                slotNamesByPath[paths[0]] = AddSourceFileRootSlotPrefix(
                    ResolvePackageName(paths[0], packageNamesByRelativePath),
                    fileStem);
                continue;
            }

            foreach (string path in paths)
            {
                slotNamesByPath[path] = AddSourceFileRootSlotPrefix(
                    ResolvePackageName(path, packageNamesByRelativePath),
                    $"{fileStem}_{ComputeStableHashSuffix(path)}");
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

    public static string ResolveRequiredSourceFileRootMeshCode(string sourceFileSlotName, string actualMeshCode)
    {
        return ResolveRequiredSourceFileRootMeshCode(null, sourceFileSlotName, actualMeshCode);
    }

    public static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    public static string AddSourceFileRootSlotPrefix(string? packageName, string slotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);

        return TryCreateSourceFileRootSlotPrefix(packageName, out string? prefix)
            ? $"{prefix} {slotName}"
            : slotName;
    }

    private static string? ResolvePackageName(
        string relativeSourceFile,
        IReadOnlyDictionary<string, string> packageNamesByRelativePath)
    {
        return packageNamesByRelativePath.TryGetValue(relativeSourceFile, out string? packageName)
            ? packageName
            : null;
    }

    private static bool TryCreateSourceFileRootSlotPrefix(string? packageName, out string? prefix)
    {
        prefix = packageName?.ToLowerInvariant() switch
        {
            "area" => "<color=hero.purple>🗺️</color>",
            "bldg" => "<color=hero.cyan>🏢</color>",
            "brid" => "<color=hero.blue>🌉</color>",
            "cons" => "<color=hero.orange>🏗️</color>",
            "dem" => "<color=mid.orange>🟫</color>",
            "fld" => "<color=mid.cyan>🌊</color>",
            "frn" => "<color=hero.orange>🚧</color>",
            "gen" => "<color=hero.purple>📦</color>",
            "htd" => "<color=hero.red>🔥</color>",
            "ifld" => "<color=hero.cyan>🌧️</color>",
            "lsld" => "<color=mid.orange>⛰️</color>",
            "luse" => "<color=hero.green>🏷️</color>",
            "rfld" => "<color=hero.blue>🌊</color>",
            "rwy" => "<color=hero.yellow>🛫</color>",
            "squr" => "<color=hero.green>🟩</color>",
            "tnm" => "<color=hero.purple>🗻</color>",
            "tran" => "<color=hero.yellow>🛣️</color>",
            "trk" => "<color=hero.orange>🚉</color>",
            "tun" => "<color=hero.purple>🚇</color>",
            "ubld" => "<color=mid.cyan>🏬</color>",
            "unf" => "<color=hero.green>🏞️</color>",
            "urf" => "<color=hero.purple>🏙️</color>",
            "veg" => "<color=hero.green>🌿</color>",
            "wtr" => "<color=mid.cyan>💧</color>",
            "wwy" => "<color=hero.cyan>⛴️</color>",
            _ => null,
        };
        return prefix is not null;
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

    public static ResoniteLocalOrigin ResolveParentOriginFromMeshRootPosition(
        string meshCode,
        ResoniteFloat3 rootPosition)
    {
        if (!PlateauMeshCode.TryGetGeodeticCenter(meshCode, out GeodeticCoordinate meshCenter))
        {
            return new ResoniteLocalOrigin(0.0, 0.0, 0.0);
        }

        ResoniteLocalOrigin origin = MoveOrigin(
            new ResoniteLocalOrigin(meshCenter.Latitude, meshCenter.Longitude, meshCenter.Altitude),
            -rootPosition.X,
            -rootPosition.Z);

        for (int iteration = 0; iteration < 6; iteration++)
        {
            ResoniteFloat3 projectedPosition = ComputeOriginOffset(
                origin,
                new ResoniteLocalOrigin(meshCenter.Latitude, meshCenter.Longitude, meshCenter.Altitude));
            double errorX = projectedPosition.X - rootPosition.X;
            double errorZ = projectedPosition.Z - rootPosition.Z;
            if (Math.Abs(errorX) <= 0.001 && Math.Abs(errorZ) <= 0.001)
            {
                break;
            }

            origin = MoveOrigin(origin, errorX, errorZ);
        }

        return origin;
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
        LocalCartesianOffset offset = GeodeticLocalProjection.Project(referenceCenter, currentCenter);
        return new ResoniteFloat3(X: offset.EastMeters, Y: 0.0, Z: offset.NorthMeters);
    }

    private static ResoniteLocalOrigin MoveOrigin(
        ResoniteLocalOrigin origin,
        double eastMeters,
        double northMeters)
    {
        GeodeticCoordinate moved = GeodeticLocalProjection.Reverse(
            new GeodeticCoordinate(origin.Latitude, origin.Longitude, origin.Altitude),
            eastMeters,
            northMeters);
        return new ResoniteLocalOrigin(moved.Latitude, moved.Longitude, moved.Altitude);
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
