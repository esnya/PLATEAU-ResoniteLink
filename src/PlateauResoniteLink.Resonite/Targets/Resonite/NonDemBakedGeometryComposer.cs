using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record NonDemBakedGeometry(
    ResoniteFloat3 Origin,
    ResoniteImportedMesh Mesh,
    IReadOnlyList<ResoniteMaterialBinding> Materials);

internal interface INonDemBakedGeometryComposer
{
    NonDemBakedGeometry Compose(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout,
        Image<Rgba32>? atlasImage,
        CancellationToken cancellationToken);
}

internal sealed class NonDemBakedGeometryComposer(ResoniteLocalOrigin requestLocalOrigin) : INonDemBakedGeometryComposer
{
    public NonDemBakedGeometry Compose(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout,
        Image<Rgba32>? atlasImage,
        CancellationToken cancellationToken)
    {
        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(sourceFileKey, candidates);
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            List<int> atlasTriangleIndices = [];
            foreach (NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement in layout.Placements
                         .OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal)
                         .ThenBy(static candidate => candidate.Entry.Submesh.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendPlacementGeometry(vertices, atlasTriangleIndices, bakeOrigin, placement, layout.Width, layout.Height);
            }

            submeshes.Add(new ResoniteMeshSubmesh(0, atlasTriangleIndices));
            materials.Add(
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TexturePayload: ResoniteTextureImportFactory.CreatePayloadFromImage(
                        atlasImage ?? throw new InvalidOperationException("Non-DEM atlas image is required when an atlas layout exists.")),
                    AssetBinding: ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv)));
        }

        foreach (IGrouping<NonDemPreservedMaterialGroupingKey, NonDemOrderedPreservedSubmeshEntry> preservedGroup in candidates
                     .SelectMany(static candidate => candidate.PreservedEntries)
                     .Select(static (entry, order) => new NonDemOrderedPreservedSubmeshEntry(entry, order))
                     .GroupBy(static entry => NonDemPreservedMaterialGrouping.CreateKey(entry.Entry.Material), NonDemPreservedMaterialGrouping.KeyComparer)
                     .OrderBy(static group => group.Min(static entry => entry.Order)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<int> preservedTriangleIndices = [];
            foreach (NonDemPreservedSubmeshEntry preservedEntry in preservedGroup
                         .Select(static entry => entry.Entry)
                         .OrderBy(static entry => entry.CityObject.SlotKey, StringComparer.Ordinal)
                         .ThenBy(static entry => entry.Submesh.Index))
            {
                AppendOriginalGeometry(vertices, preservedTriangleIndices, bakeOrigin, preservedEntry);
            }

            if (preservedTriangleIndices.Count == 0)
            {
                continue;
            }

            int submeshIndex = submeshes.Count;
            ResoniteMaterialBinding preservedMaterial = NonDemPreservedMaterialGrouping.NormalizeMaterial(preservedGroup.First().Entry.Material) with
            {
                SubmeshIndices = [submeshIndex],
            };
            submeshes.Add(new ResoniteMeshSubmesh(submeshIndex, preservedTriangleIndices));
            materials.Add(preservedMaterial);
        }

        if (submeshes.Count == 0 || materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Non-DEM bake batch '{sourceFileKey.PackageName}:{sourceFileKey.ActualMeshCode}:LOD{sourceFileKey.LodLevel}' produced no materialized submesh.");
        }

        return new NonDemBakedGeometry(
            bakeOrigin,
            new ResoniteImportedMesh(vertices, submeshes),
            materials);
    }

    private static void AppendPlacementGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement,
        int atlasWidth,
        int atlasHeight)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = placement.Entry.CityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(placement.Entry.CityObject.Transform.Position, bakeOrigin);
        NonDemAtlasRect innerRect = placement.InnerRect;

        foreach (int sourceIndex in placement.Entry.Submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            ResoniteFloat2 sourceUv = sourceVertex.UV0;
            ResoniteFloat2 atlasUv = MapUvToAtlas(sourceUv, placement.Entry.UvBounds, innerRect, atlasWidth, atlasHeight);
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
                UV0 = atlasUv,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    private static void AppendOriginalGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        NonDemPreservedSubmeshEntry preservedEntry)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = preservedEntry.CityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(preservedEntry.CityObject.Transform.Position, bakeOrigin);
        foreach (int sourceIndex in preservedEntry.Submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
                Color = preservedEntry.VertexColorOverride ?? sourceVertex.Color,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    private static ResoniteFloat2 MapUvToAtlas(
        ResoniteFloat2 sourceUv,
        TextureUvRect uvBounds,
        NonDemAtlasRect atlasRect,
        double atlasWidth,
        double atlasHeight)
    {
        TextureUvRect atlasUvRect = TextureUvRect.FromTopLeftPixelRect(
            atlasRect.X,
            atlasRect.Y,
            atlasRect.Width,
            atlasRect.Height,
            (int)Math.Round(atlasWidth),
            (int)Math.Round(atlasHeight));
        ScalarPair remapped = TextureUvRect.RemapValue(
            new ScalarPair(sourceUv.X, sourceUv.Y),
            uvBounds,
            atlasUvRect);
        return new ResoniteFloat2(remapped.X, remapped.Y);
    }

    private ResoniteFloat3 ComputeBakeOrigin(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
    {
        if (ThirdRegionalMeshCode.TryParse(sourceFileKey.ActualMeshCode, out _)
            && PlateauMeshCode.TryGetGeodeticCenter(sourceFileKey.ActualMeshCode, out GeodeticCoordinate meshCenter))
        {
            return ResonitePlacementPolicy.ComputeOriginOffset(
                requestLocalOrigin,
                new ResoniteLocalOrigin(meshCenter.Latitude, meshCenter.Longitude, meshCenter.Altitude));
        }

        return ComputeGeometryMinimumOrigin(candidates);
    }

    private static ResoniteFloat3 ComputeGeometryMinimumOrigin(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;

        foreach (ResoniteConstructionCityObject cityObject in candidates.Select(static candidate => candidate.CityObject))
        {
            foreach (ResoniteMeshVertex vertex in cityObject.Mesh.Vertices)
            {
                ResoniteFloat3 worldPosition = Add(vertex.Position, cityObject.Transform.Position);
                minX = Math.Min(minX, worldPosition.X);
                minY = Math.Min(minY, worldPosition.Y);
                minZ = Math.Min(minZ, worldPosition.Z);
            }
        }

        return double.IsPositiveInfinity(minX)
            ? new ResoniteFloat3(0.0, 0.0, 0.0)
            : new ResoniteFloat3(minX, minY, minZ);
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }
}
