using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;


using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Source;

namespace PlateauResoniteLink.Application.Importing.Plateau;

internal static class CityGmlDemTerrainGridCityObjectProjection
{
    internal static bool TryProject(
        ConstructionCityObjectDraft cityObject,
        ConstructionCityObjectDraft terrainGridSamplingSource,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        CancellationToken cancellationToken,
        out bool outsideSamplingBounds,
        out TerrainGridProjectedCityObject? heightMapCityObject)
    {
        cancellationToken.ThrowIfCancellationRequested();
        outsideSamplingBounds = false;
        heightMapCityObject = null;

        if (terrainGridSamplingSource.Surfaces.SelectMany(static surface => surface.Vertices).Take(3).Count() < 3)
        {
            return false;
        }

        if (!TryCreateThirdMeshOutputFrame(
                cityObject,
                out DemTerrainThirdMeshOutputFrame thirdMeshFrame))
        {
            return false;
        }

        DemTerrainResoniteEmissionFrame thirdMeshEmissionFrame = CreateThirdMeshEmissionFrame(
            thirdMeshFrame,
            globalOriginPoint,
            globalCartesian);
        DemTerrainSourceFileFrame sourceFileFrame = CreateSourceFileFrame(
            terrainGridSamplingSource,
            thirdMeshFrame);
        if (sourceFileFrame.LocalPositions.Length < 3)
        {
            return false;
        }

        if (!TryCreateDemTerrainGridBounds(
            cityObject,
            terrainGridSamplingSource,
            thirdMeshFrame,
            demTerrainTextureOverlay,
            requestedMeshCodeBounds,
            sourceFileFrame.LocalPositions,
            out DemTerrainGridBounds? heightMapBounds)
            || heightMapBounds is null)
        {
            outsideSamplingBounds = true;
            return false;
        }

        if (!DemTerrainGridOutputFrame.TryCreate(heightMapBounds, out DemTerrainGridOutputFrame outputFrame))
        {
            return false;
        }

        int width = Math.Clamp(
            (int)Math.Ceiling(outputFrame.ExtentX / request.TerrainGridMetersPerVertex) + 1,
            2,
            request.TerrainGridMaxResolution);
        int height = Math.Clamp(
            (int)Math.Ceiling(outputFrame.ExtentZ / request.TerrainGridMetersPerVertex) + 1,
            2,
            request.TerrainGridMaxResolution);
        PlateauDiagnostics.Verbose(
            "Sampling DEM terrain grid '{SlotKey}' (width={Width}, height={Height}, triangles={TriangleCount}).",
            cityObject.SlotKey,
            width,
            height,
            sourceFileFrame.Triangles.Length);

        DemTerrainGridHeightSamples heightSamples = CityGmlDemTerrainGridSampler.Sample(
            outputFrame.SamplingBounds,
            request.TerrainGridMetersPerVertex,
            request.TerrainGridMaxResolution,
            sourceFileFrame.SeaLevelLocalHeight,
            sourceFileFrame.Triangles,
            CreateSeaLevelFallbackHeightProvider(thirdMeshEmissionFrame, thirdMeshFrame.Cartesian, globalCartesian),
            cancellationToken);
        width = heightSamples.Width;
        height = heightSamples.Height;
        DemTerrainGridHeightFrame heightFrame = DemTerrainGridHeightFrame.Create(heightSamples);

        MaterialBinding[] materials = CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
            cityObject,
            thirdMeshFrame.Origin,
            thirdMeshFrame.Cartesian,
            demTerrainTextureOverlay,
            materialResolver);
        if (materials.Length == 0)
        {
            return false;
        }

        TextureUvRect? heightMapOccupiedUvRect = TryCreateDemTerrainGridOccupiedUvRect(
            cityObject,
            thirdMeshFrame.Origin,
            thirdMeshFrame.Cartesian,
            demTerrainTextureOverlay,
            heightMapBounds.GeographicBounds,
            materialResolver);
        Float2? heightMapUvScale = heightMapOccupiedUvRect.HasValue
            ? ToContractFloat2(heightMapOccupiedUvRect.Value.ScaleValue)
            : null;
        Float2? heightMapUvOffset = heightMapOccupiedUvRect.HasValue
            ? ToContractFloat2(heightMapOccupiedUvRect.Value.OffsetValue)
            : null;

        DemTerrainResoniteEmissionFrame emissionFrame = DemTerrainResoniteEmissionFrame.Create(
            thirdMeshEmissionFrame,
            outputFrame,
            heightFrame);

        TerrainGridGeometry geometry = new(
            Width: width,
            Height: height,
            Size: new Float2(outputFrame.ExtentX, outputFrame.ExtentZ),
            MinHeight: heightFrame.MinHeight,
            MaxHeight: heightFrame.MaxHeight,
            HeightSamples: heightFrame.LocalHeights,
            SampleCoverage: heightFrame.SampleCoverage,
            UvScale: heightMapUvScale,
            UvOffset: heightMapUvOffset);
        ImportedCityObject projectedCityObject = new(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(emissionFrame.SlotPosition)),
            Geometry: geometry,
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        heightMapCityObject = new TerrainGridProjectedCityObject(projectedCityObject, geometry);
        return true;
    }

    private static bool TryCreateThirdMeshOutputFrame(
        ConstructionCityObjectDraft cityObject,
        out DemTerrainThirdMeshOutputFrame frame)
    {
        GeodeticPoint origin = DemTerrainObjectFrameResolver.ResolveRequiredThirdMeshOrigin(
            cityObject.ActualMeshCode,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
        LocalCartesian? cartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                origin.Latitude,
                origin.Longitude,
                origin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        if (cartesian is null)
        {
            frame = default!;
            return false;
        }

        frame = new DemTerrainThirdMeshOutputFrame(origin, cartesian);
        return true;
    }

    private static DemTerrainResoniteEmissionFrame CreateThirdMeshEmissionFrame(
        DemTerrainThirdMeshOutputFrame thirdMeshFrame,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        return new DemTerrainResoniteEmissionFrame(CreateScenePosition(
            thirdMeshFrame.Origin,
            globalOriginPoint,
            globalCartesian));
    }

    private static DemTerrainSourceFileFrame CreateSourceFileFrame(
        ConstructionCityObjectDraft terrainGridSamplingSource,
        DemTerrainThirdMeshOutputFrame thirdMeshFrame)
    {
        Float3[] positions = terrainGridSamplingSource.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(thirdMeshFrame.CreateLocalPosition)
            .ToArray();
        TerrainGridTriangle[] triangles = CreateDemTerrainGridTriangles(
            terrainGridSamplingSource,
            thirdMeshFrame);
        return new DemTerrainSourceFileFrame(
            positions,
            triangles,
            thirdMeshFrame.SeaLevelLocalHeight);
    }

    private static TextureUvRect? TryCreateDemTerrainGridOccupiedUvRect(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle terrainGridGeographicBounds,
        IDefaultMaterialResolver materialResolver)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        ResolvedSurfaceMaterial? representativeSurface = CityGmlSurfaceMaterialResolver.EnumerateSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver)
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Material.TerrainOverlay is not null);
        if (representativeSurface is null)
        {
            return null;
        }

        TextureUvRect? occupiedUvRect = DemTerrainOverlayUvMapper.TryCreateTerrainGridOccupiedUvRect(
            cityObject.Source,
            representativeSurface,
            demTerrainTextureOverlay,
            terrainGridGeographicBounds);
        return occupiedUvRect is { IsIdentity: true } ? null : occupiedUvRect;
    }

    private static bool TryCreateDemTerrainGridBounds(
        ConstructionCityObjectDraft cityObject,
        ConstructionCityObjectDraft terrainGridSamplingSource,
        DemTerrainThirdMeshOutputFrame frame,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IReadOnlyList<Float3> positions,
        out DemTerrainGridBounds? bounds)
    {
        bounds = null;
        double rawMinX = positions.Min(static position => position.X);
        double rawMaxX = positions.Max(static position => position.X);
        double rawMinZ = positions.Min(static position => position.Z);
        double rawMaxZ = positions.Max(static position => position.Z);

        GeographicRectangle clippedBounds = ResolveDemTerrainGridGeographicBounds(
            cityObject,
            demTerrainTextureOverlay,
            requestedMeshCodeBounds);
        GeographicRectangle rawGeographicBounds = ResolveCityObjectGeographicBounds(terrainGridSamplingSource.Source);
        GeographicRectangle clampedGeographicBounds = IntersectGeographicBounds(clippedBounds, rawGeographicBounds);
        bool rawNearlyCoversClippedBounds = NearlyCoversGeographicBounds(rawGeographicBounds, clippedBounds);
        if (rawNearlyCoversClippedBounds)
        {
            clampedGeographicBounds = clippedBounds;
        }

        if (!IsUsableGeographicBounds(clampedGeographicBounds))
        {
            clampedGeographicBounds = rawGeographicBounds;
        }

        Float3[] clippedAxisPositions = CreateClippedAxisPositions(
            clippedBounds,
            frame);

        double clippedMinX = Math.Min(clippedAxisPositions[0].X, clippedAxisPositions[1].X);
        double clippedMaxX = Math.Max(clippedAxisPositions[0].X, clippedAxisPositions[1].X);
        double clippedMinZ = Math.Min(clippedAxisPositions[2].Z, clippedAxisPositions[3].Z);
        double clippedMaxZ = Math.Max(clippedAxisPositions[2].Z, clippedAxisPositions[3].Z);

        if (!rawNearlyCoversClippedBounds)
        {
            clippedMinX = Math.Max(clippedMinX, rawMinX);
            clippedMaxX = Math.Min(clippedMaxX, rawMaxX);
            clippedMinZ = Math.Max(clippedMinZ, rawMinZ);
            clippedMaxZ = Math.Min(clippedMaxZ, rawMaxZ);
        }

        if ((clippedMaxX - clippedMinX) <= 1e-6 || (clippedMaxZ - clippedMinZ) <= 1e-6)
        {
            if (cityObject.Source.SharedAcrossMeshCodes)
            {
                return false;
            }

            bounds = new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ, clampedGeographicBounds);
            return true;
        }

        bounds = new DemTerrainGridBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ, clampedGeographicBounds);
        return true;
    }

    private static GeographicRectangle ResolveDemTerrainGridGeographicBounds(
        ConstructionCityObjectDraft cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        GeographicRectangle bounds = TryCreateThirdMeshGeographicBounds(cityObject.ActualMeshCode, out GeographicRectangle thirdMeshBounds)
            ? thirdMeshBounds
            : ResolveCityObjectGeographicBounds(cityObject.Source);

        if (requestedMeshCodeBounds.Count > 0)
        {
            bounds = IntersectRequestedMeshBounds(bounds, requestedMeshCodeBounds)
                ?? bounds;
        }

        return demTerrainTextureOverlay is null
            ? bounds
            : IntersectGeographicBounds(bounds, demTerrainTextureOverlay.GeographicBounds);
    }

    private static GeographicRectangle? IntersectRequestedMeshBounds(
        GeographicRectangle bounds,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        GeographicRectangle[] intersections = requestedMeshCodeBounds
            .Select(requested => IntersectGeographicBounds(bounds, new GeographicRectangle(
                requested.SouthLatitude,
                requested.NorthLatitude,
                requested.WestLongitude,
                requested.EastLongitude)))
            .Where(static intersection => IsUsableGeographicBounds(intersection))
            .ToArray();
        return intersections.Length == 0
            ? null
            : new GeographicRectangle(
                intersections.Min(static intersection => intersection.MinLatitude),
                intersections.Max(static intersection => intersection.MaxLatitude),
                intersections.Min(static intersection => intersection.MinLongitude),
                intersections.Max(static intersection => intersection.MaxLongitude));
    }

    private static bool TryCreateThirdMeshGeographicBounds(
        string actualMeshCode,
        out GeographicRectangle bounds)
    {
        if (ThirdRegionalMeshCode.TryParse(actualMeshCode, out ThirdRegionalMeshCode thirdMeshCode))
        {
            JisRegionalMeshBounds meshBounds = thirdMeshCode.Bounds;
            bounds = new GeographicRectangle(
                meshBounds.SouthLatitude,
                meshBounds.NorthLatitude,
                meshBounds.WestLongitude,
                meshBounds.EastLongitude);
            return true;
        }

        bounds = new GeographicRectangle(0.0, 0.0, 0.0, 0.0);
        return false;
    }

    private static Float3[] CreateClippedAxisPositions(
        GeographicRectangle clippedBounds,
        DemTerrainThirdMeshOutputFrame frame)
    {
        double boundsAltitude = frame.Origin.Altitude;
        double referenceLatitude = frame.Origin.Latitude;
        double referenceLongitude = frame.Origin.Longitude;
        return
        [
            frame.CreateLocalPosition(new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, boundsAltitude)),
            frame.CreateLocalPosition(new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, boundsAltitude)),
            frame.CreateLocalPosition(new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, boundsAltitude)),
            frame.CreateLocalPosition(new GeodeticPoint(clippedBounds.MaxLatitude, referenceLongitude, boundsAltitude)),
        ];
    }

    private static GeographicRectangle ResolveCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        return CityObjectGeographicBoundsResolver.Resolve(
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static GeographicRectangle IntersectGeographicBounds(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return new GeographicRectangle(
            MinLatitude: Math.Max(left.MinLatitude, right.MinLatitude),
            MaxLatitude: Math.Min(left.MaxLatitude, right.MaxLatitude),
            MinLongitude: Math.Max(left.MinLongitude, right.MinLongitude),
            MaxLongitude: Math.Min(left.MaxLongitude, right.MaxLongitude));
    }

    private static bool IsUsableGeographicBounds(GeographicRectangle bounds)
    {
        return (bounds.MaxLatitude - bounds.MinLatitude) > 1e-12
            && (bounds.MaxLongitude - bounds.MinLongitude) > 1e-12;
    }

    private static bool NearlyCoversGeographicBounds(
        GeographicRectangle candidate,
        GeographicRectangle bounds)
    {
        const double relativeTolerance = 1e-3;
        double latitudeTolerance = Math.Max(bounds.MaxLatitude - bounds.MinLatitude, 0.0) * relativeTolerance;
        double longitudeTolerance = Math.Max(bounds.MaxLongitude - bounds.MinLongitude, 0.0) * relativeTolerance;
        return candidate.MinLatitude <= bounds.MinLatitude + latitudeTolerance
            && candidate.MaxLatitude >= bounds.MaxLatitude - latitudeTolerance
            && candidate.MinLongitude <= bounds.MinLongitude + longitudeTolerance
            && candidate.MaxLongitude >= bounds.MaxLongitude - longitudeTolerance;
    }

    private static TerrainGridTriangle[] CreateDemTerrainGridTriangles(
        ConstructionCityObjectDraft cityObject,
        DemTerrainThirdMeshOutputFrame frame)
    {
        List<TerrainGridTriangle> triangles = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            Float3[] positions = surface.ExteriorRing.Vertices
                .Select(frame.CreateLocalPosition)
                .ToArray();
            if (positions.Length < 3)
            {
                continue;
            }

            Float3 origin = positions[0];
            for (int index = 1; index + 1 < positions.Length; index++)
            {
                triangles.Add(new TerrainGridTriangle(origin, positions[index], positions[index + 1]));
            }
        }

        return triangles.ToArray();
    }

    private static Func<double, double, double>? CreateSeaLevelFallbackHeightProvider(
        DemTerrainResoniteEmissionFrame emissionFrame,
        LocalCartesian cityObjectCartesian,
        LocalCartesian? globalCartesian)
    {
        if (globalCartesian is null)
        {
            return null;
        }

        return (localX, localZ) =>
        {
            (double latitude, double longitude, _) = cityObjectCartesian.Reverse(
                localX,
                localZ,
                0.0);
            (_, _, double seaLevelUp) = globalCartesian.Forward(
                latitude,
                longitude,
                0.0);
            return seaLevelUp - emissionFrame.SlotPosition.Y;
        };
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    private sealed record DemTerrainGridBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ,
        GeographicRectangle GeographicBounds);

    private sealed record DemTerrainThirdMeshOutputFrame(
        GeodeticPoint Origin,
        LocalCartesian Cartesian)
    {
        public double SeaLevelLocalHeight => CreateLocalPosition(
            new GeodeticPoint(Origin.Latitude, Origin.Longitude, 0.0)).Y;

        public Float3 CreateLocalPosition(GeodeticPoint point)
        {
            return CreateScenePosition(point, Origin, Cartesian);
        }
    }

    private sealed record DemTerrainSourceFileFrame(
        Float3[] LocalPositions,
        TerrainGridTriangle[] Triangles,
        double SeaLevelLocalHeight);

    private sealed record DemTerrainGridOutputFrame(
        TerrainGridSamplingBounds SamplingBounds,
        double CenterX,
        double CenterZ,
        double ExtentX,
        double ExtentZ)
    {
        public static bool TryCreate(DemTerrainGridBounds bounds, out DemTerrainGridOutputFrame frame)
        {
            double extentX = bounds.MaxX - bounds.MinX;
            double extentZ = bounds.MaxZ - bounds.MinZ;
            if (extentX <= 1e-6 || extentZ <= 1e-6)
            {
                frame = default!;
                return false;
            }

            frame = new DemTerrainGridOutputFrame(
                new TerrainGridSamplingBounds(bounds.MinX, bounds.MaxX, bounds.MinZ, bounds.MaxZ),
                CenterX: (bounds.MinX + bounds.MaxX) / 2.0,
                CenterZ: (bounds.MinZ + bounds.MaxZ) / 2.0,
                extentX,
                extentZ);
            return true;
        }
    }

    private sealed record DemTerrainGridHeightFrame(
        double VerticalOriginLocalHeight,
        double[] LocalHeights,
        TerrainGridSampleCoverage[] SampleCoverage,
        double MinHeight,
        double MaxHeight)
    {
        public static DemTerrainGridHeightFrame Create(DemTerrainGridHeightSamples samples)
        {
            double sourceMinHeight = samples.LocalHeights.Min();
            double sourceMaxHeight = samples.LocalHeights.Max();
            double verticalOriginLocalHeight = (sourceMinHeight + sourceMaxHeight) / 2.0;
            double[] localHeights = samples.LocalHeights
                .Select(height => height - verticalOriginLocalHeight)
                .ToArray();
            return new DemTerrainGridHeightFrame(
                verticalOriginLocalHeight,
                localHeights,
                samples.SampleCoverage,
                localHeights.Min(),
                localHeights.Max());
        }
    }

    private sealed record DemTerrainResoniteEmissionFrame(Float3 SlotPosition)
    {
        public static DemTerrainResoniteEmissionFrame Create(
            DemTerrainResoniteEmissionFrame thirdMeshEmissionFrame,
            DemTerrainGridOutputFrame outputFrame,
            DemTerrainGridHeightFrame heightFrame)
        {
            Float3 slotPosition = thirdMeshEmissionFrame.SlotPosition with
            {
                X = thirdMeshEmissionFrame.SlotPosition.X + outputFrame.CenterX,
                Y = thirdMeshEmissionFrame.SlotPosition.Y + heightFrame.VerticalOriginLocalHeight,
                Z = thirdMeshEmissionFrame.SlotPosition.Z + outputFrame.CenterZ,
            };
            return new DemTerrainResoniteEmissionFrame(slotPosition);
        }
    }
}

internal sealed record TerrainGridProjectedCityObject(
    ImportedCityObject CityObject,
    TerrainGridGeometry Geometry);
