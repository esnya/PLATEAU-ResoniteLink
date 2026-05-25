using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBakeAssembler(
    NonDemAtlasLayoutFactory atlasLayoutFactory,
    NonDemAtlasImageRenderer atlasImageRenderer)
{
    public Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken)
    {
        List<NonDemAtlasBatchEntry> entries = candidates.SelectMany(static candidate => candidate.AtlasEntries).ToList();
        NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout = null;
        if (entries.Count > 0
            && (!atlasLayoutFactory.TryCreate(entries, out layout) || layout is null))
        {
            throw new InvalidOperationException("Failed to create non-DEM atlas layout.");
        }

        using Image<Rgba32>? atlasImage = layout is null
            ? null
            : new Image<Rgba32>(layout.Width, layout.Height, new Rgba32(0, 0, 0, 0));
        if (layout is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            atlasImageRenderer.Draw(atlasImage!, layout.Placements);
        }

        ResoniteConstructionCityObject firstCityObject = candidates[0].CityObject;
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : NonDemSourceFileBatching.CreateBatchSlotKey(sourceFileKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : NonDemSourceFileBatching.CreateBatchDisplayName(sourceFileKey, batchIndex, slotKey);
        string? sourceFileRelativePath = string.IsNullOrWhiteSpace(firstCityObject.SourceFileRelativePath)
            ? null
            : sourceFileKey.SourceFileRelativePath;

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(candidates);
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            string textureIdentity = NonDemSourceFileBatching.CreateAtlasTextureIdentity(sourceFileKey, batchIndex);
            List<int> atlasTriangleIndices = [];
            foreach (NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement in layout.Placements.OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal).ThenBy(static candidate => candidate.Entry.Submesh.Index))
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
                    TexturePayload: ResoniteTextureImportFactory.CreatePayloadFromImage(atlasImage!, identity: textureIdentity),
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv));
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

        ResoniteConstructionCityObject bakedCityObject = new(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: firstCityObject.PackageName,
            ActualMeshCode: firstCityObject.ActualMeshCode,
            LodLevel: firstCityObject.LodLevel,
            Transform: new ResoniteTransform(bakeOrigin),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials,
            CollisionEnabled: candidates.Any(static candidate => candidate.CityObject.CollisionEnabled),
            SourceFileRelativePath: sourceFileRelativePath);
        return Task.FromResult(bakedCityObject);
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

    private static ResoniteFloat3 ComputeBakeOrigin(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
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
