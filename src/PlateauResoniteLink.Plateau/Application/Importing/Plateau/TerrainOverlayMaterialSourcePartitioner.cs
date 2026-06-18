using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using GeographicLib;


using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

namespace PlateauResoniteLink.Plateau.Application.Importing.Plateau;

internal static class TerrainOverlayMaterialSourcePartitioner
{
    internal static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> PartitionParsedCityObject(
        ParsedCityObject cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        bool allowMissingGeneratedDemOverlayCoverage = false,
        CancellationToken cancellationToken = default)
    {
        foreach ((ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay) partitionedCityObject
                 in PartitionConstructionCityObject(
                     ConstructionCityObjectDraft.FromParsedCityObject(cityObject),
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     allowMissingGeneratedDemOverlayCoverage, cancellationToken))
        {
            yield return (partitionedCityObject.CityObject.Source, partitionedCityObject.Overlay);
        }
    }

    internal static IEnumerable<(ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay)> PartitionConstructionCityObject(
        ConstructionCityObjectDraft cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        bool allowMissingGeneratedDemOverlayCoverage = false,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) partitionedCityObject
                     in DemTerrainOverlayAssignment.SplitParsedCityObject(
                         cityObject.Source,
                         demTerrainTextureOverlays,
                         requestedMeshCodeBounds,
                         allowMissingGeneratedDemOverlayCoverage, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (ConstructionCityObjectDraft.FromParsedCityObject(partitionedCityObject.CityObject), partitionedCityObject.Overlay);
            }

            yield break;
        }

        foreach ((ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay) nonDemPartition
                 in PartitionBuildingByTerrainOverlayMaterialSource(
                     cityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds, cancellationToken))
        {
            yield return nonDemPartition;
        }
    }

    private static IEnumerable<(ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay)> PartitionBuildingByTerrainOverlayMaterialSource(
        ConstructionCityObjectDraft cityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        CancellationToken cancellationToken)
    {
        if (demTerrainTextureOverlays.Count == 0 || !PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName))
        {
            yield return (cityObject, null);
            yield break;
        }

        GeodeticPoint cityObjectOrigin = GetCityObjectOrigin(cityObject.Source);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        GeodeticPoint[] cityObjectVertices = [.. cityObject.Surfaces.SelectMany(static surface => surface.Vertices)];
        if (cityObjectVertices.Length == 0)
        {
            yield return (cityObject, null);
            yield break;
        }

        double cityObjectMinAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(cityObjectVertices);
        TerrainMaterialSourceMeshCode materialSourceMeshCode = TerrainMaterialSourceMeshCode.ParseRequired(
            string.IsNullOrWhiteSpace(cityObject.Source.SourceMeshCode)
                ? cityObject.ActualMeshCode
                : cityObject.Source.SourceMeshCode);

        List<ConstructionFace> untexturedFaces = [];
        List<(ConstructionFace Face, TerrainTextureOverlay Overlay)> terrainOverlayMaterialFaces = [];
        foreach (ConstructionFace face in cityObject.Faces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedSurface surface = face.Surface;
            if (!CanUseTerrainOverlayMaterialSource(face, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian)
                || !TryCreateSurfaceGeographicBounds(surface, out GeographicRectangle surfaceBounds))
            {
                untexturedFaces.Add(face);
                continue;
            }

            ConstructionFace terrainMaterialFace = PrepareTerrainOverlayMaterialFace(face);
            ParsedSurface terrainMaterialSurface = terrainMaterialFace.Surface;

            TerrainTextureOverlay[] materialSourceOverlays = demTerrainTextureOverlays
                .Where(overlay => IsOverlayInMaterialSourceMeshCode(materialSourceMeshCode, overlay))
                .ToArray();
            TerrainTextureOverlay[] candidateOverlays = materialSourceOverlays.Length == 0
                ? demTerrainTextureOverlays
                    .Where(overlay => TerrainOverlayMeshCodeResolver.IsRequestedOverlay(overlay, requestedMeshCodeBounds))
                    .Where(overlay => TerrainOverlayMeshCodeResolver.BoundsOverlap(surfaceBounds, overlay.GeographicBounds))
                    .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                    .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                    .ToArray()
                : materialSourceMeshCode.IsThirdRegionalMesh
                    ? materialSourceOverlays
                        .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                        .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                        .ToArray()
                : materialSourceOverlays
                    .Where(overlay => TerrainOverlayMeshCodeResolver.BoundsOverlap(surfaceBounds, overlay.GeographicBounds))
                    .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                    .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                    .ToArray();
            if (candidateOverlays.Length == 0)
            {
                untexturedFaces.Add(face);
                continue;
            }

            if (candidateOverlays.Length == 1)
            {
                terrainOverlayMaterialFaces.Add((terrainMaterialFace, candidateOverlays[0]));
                continue;
            }

            TerrainTextureOverlay? containingOverlay = candidateOverlays.FirstOrDefault(overlay =>
                TerrainOverlayMeshCodeResolver.ContainsBounds(overlay.GeographicBounds, surfaceBounds));
            if (containingOverlay is not null)
            {
                terrainOverlayMaterialFaces.Add((terrainMaterialFace, containingOverlay));
                continue;
            }

            IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                    terrainMaterialSurface,
                    candidateOverlays, cancellationToken);
            if (clippedSurfaces.Count == 0)
            {
                untexturedFaces.Add(face);
                continue;
            }

            terrainOverlayMaterialFaces.AddRange(
                clippedSurfaces.Select(entry => (WithSurface(terrainMaterialFace, entry.Surface), entry.Overlay)));
        }

        IGrouping<TerrainTextureOverlay, (ConstructionFace Face, TerrainTextureOverlay Overlay)>[] terrainMaterialGroups =
            terrainOverlayMaterialFaces
                .GroupBy(static entry => entry.Overlay)
                .OrderBy(static group => group.Key.GeographicBounds.MinLatitude)
                .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
                .ToArray();
        int partitionCount = terrainMaterialGroups.Length + (untexturedFaces.Count == 0 ? 0 : 1);
        if (partitionCount == 0)
        {
            yield break;
        }

        if (partitionCount == 1)
        {
            if (terrainMaterialGroups.Length == 1)
            {
                ThirdRegionalMeshCode terrainMeshCode = terrainMaterialGroups[0].Key.MeshCode;
                yield return (
                    WithSource(
                        cityObject,
                        cityObject.Source with
                        {
                            ActualMeshCode = terrainMeshCode.Value,
                            GeodeticOriginOverride = cityObjectOrigin,
                        },
                        terrainMaterialGroups[0]
                            .Select(static entry => MarkTerrainOverlayMaterialSource(entry.Face))
                            .ToArray()),
                    terrainMaterialGroups[0].Key);
                yield break;
            }

            yield return (
                WithSource(
                    cityObject,
                    cityObject.Source with { GeodeticOriginOverride = cityObjectOrigin },
                    untexturedFaces.ToArray()),
                null);
            yield break;
        }

        int partitionIndex = 0;
        foreach (IGrouping<TerrainTextureOverlay, (ConstructionFace Face, TerrainTextureOverlay Overlay)> group in terrainMaterialGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThirdRegionalMeshCode terrainMeshCode = group.Key.MeshCode;
            yield return (
                WithSource(
                    cityObject,
                    cityObject.Source with
                    {
                        ActualMeshCode = terrainMeshCode.Value,
                        SlotKey = $"{cityObject.SlotKey}_terrain_{terrainMeshCode}",
                        DisplayName = $"{cityObject.DisplayName} ({partitionIndex + 1})",
                        GeodeticOriginOverride = cityObjectOrigin,
                    },
                    group
                        .Select(static entry => MarkTerrainOverlayMaterialSource(entry.Face))
                        .ToArray()),
                group.Key);
            partitionIndex++;
        }

        if (untexturedFaces.Count != 0)
        {
            yield return (
                WithSource(
                    cityObject,
                    cityObject.Source with
                    {
                        SlotKey = $"{cityObject.SlotKey}_terrain_none",
                        DisplayName = $"{cityObject.DisplayName} ({partitionIndex + 1})",
                        GeodeticOriginOverride = cityObjectOrigin,
                    },
                    untexturedFaces.ToArray()),
                null);
        }
    }

    private static ConstructionFace MarkTerrainOverlayMaterialSource(ConstructionFace face)
    {
        return face with { MaterialTreatment = SurfaceMaterialTreatment.TerrainOverlayMaterialSource };
    }

    private static ConstructionFace PrepareTerrainOverlayMaterialFace(ConstructionFace face)
    {
        ParsedSurface surface = face.Surface;
        if (face.Role is ConstructionFaceRole.RoofSlab)
        {
            return face;
        }

        return surface.Semantic == ParsedSurfaceSemantic.Roof
            ? face
            : WithSurface(face, surface with { Semantic = ParsedSurfaceSemantic.Roof });
    }

    private static ConstructionCityObjectDraft WithSource(
        ConstructionCityObjectDraft cityObject,
        ParsedCityObject source,
        ConstructionFace[] faces)
    {
        return new ConstructionCityObjectDraft(source, faces, cityObject.FacadeUvReferenceFaces);
    }

    private static ConstructionFace WithSurface(ConstructionFace face, ParsedSurface surface)
    {
        return face with { Surface = surface };
    }

    private static bool IsOverlayInMaterialSourceMeshCode(
        TerrainMaterialSourceMeshCode materialSourceMeshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        return materialSourceMeshCode.Contains(terrainOverlay.MeshCode);
    }

    private static bool CanUseTerrainOverlayMaterialSource(
        ConstructionFace face,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ParsedSurface surface = face.Surface;
        return surface.TexturePayload is null
            && RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
                face,
                cityObjectMinAltitude,
                cityObjectOrigin,
                cityObjectCartesian);
    }

    private static bool TryCreateSurfaceGeographicBounds(
        ParsedSurface surface,
        out GeographicRectangle bounds)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length == 0)
        {
            bounds = new GeographicRectangle(0.0, 0.0, 0.0, 0.0);
            return false;
        }

        bounds = new GeographicRectangle(
            vertices.Min(static vertex => vertex.Latitude),
            vertices.Max(static vertex => vertex.Latitude),
            vertices.Min(static vertex => vertex.Longitude),
            vertices.Max(static vertex => vertex.Longitude));
        return true;
    }

    private static GeodeticPoint GetCityObjectOrigin(ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private abstract record TerrainMaterialSourceMeshCode
    {
        public abstract bool IsThirdRegionalMesh { get; }

        public static TerrainMaterialSourceMeshCode ParseRequired(string meshCode)
        {
            if (ThirdRegionalMeshCode.TryParse(meshCode, out ThirdRegionalMeshCode thirdMeshCode))
            {
                return new Third(thirdMeshCode);
            }

            if (SecondRegionalMeshCode.TryParse(meshCode, out SecondRegionalMeshCode secondMeshCode))
            {
                return new Second(secondMeshCode);
            }

            throw new PlateauImportValidationException(
                [$"Terrain overlay material source mesh-code '{meshCode}' must be a valid second- or third-level mesh-code."]);
        }

        public abstract bool Contains(ThirdRegionalMeshCode overlayMeshCode);

        private sealed record Third(ThirdRegionalMeshCode MeshCode) : TerrainMaterialSourceMeshCode
        {
            public override bool IsThirdRegionalMesh => true;

            public override bool Contains(ThirdRegionalMeshCode overlayMeshCode)
            {
                return MeshCode == overlayMeshCode;
            }
        }

        private sealed record Second(SecondRegionalMeshCode MeshCode) : TerrainMaterialSourceMeshCode
        {
            public override bool IsThirdRegionalMesh => false;

            public override bool Contains(ThirdRegionalMeshCode overlayMeshCode)
            {
                return MeshCode == overlayMeshCode.Parent;
            }
        }
    }
}
