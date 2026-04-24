using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class SceneImportContractMapper
{
    internal static ResoniteMaterialBinding[] ToInternal(IReadOnlyList<MaterialBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return bindings.Select(ToInternal).ToArray();
    }

    internal static ResoniteConstructionCityObject ToInternal(ImportedCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            TriangleMeshGeometry triangleMesh => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                ToInternal(triangleMesh.Mesh),
                cityObject.Materials.Select(ToInternal).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath),
            TerrainGridGeometry heightMap => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                new ResoniteTerrainGridGeometry(
                    heightMap.Width,
                    heightMap.Height,
                    ToInternal(heightMap.Size),
                    heightMap.MinHeight,
                    heightMap.MaxHeight,
                    heightMap.HeightSamples,
                    heightMap.UvScale is null ? null : ToInternal(heightMap.UvScale),
                    heightMap.UvOffset is null ? null : ToInternal(heightMap.UvOffset)),
                cityObject.Materials.Select(ToInternal).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    private static ResoniteTransform ToInternal(Transform3D transform)
    {
        return new ResoniteTransform(
            ToInternal(transform.Position),
            transform.Rotation is null ? null : ToInternal(transform.Rotation));
    }

    private static ResoniteFloat2 ToInternal(Float2 value) => new(value.X, value.Y);

    private static ResoniteFloat3 ToInternal(Float3 value) => new(value.X, value.Y, value.Z);

    private static ResoniteFloatQ ToInternal(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static ResoniteColor ToInternal(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static ResoniteImportedMesh ToInternal(ImportedMesh mesh)
    {
        return new ResoniteImportedMesh(
            mesh.Vertices.Select(ToInternal).ToArray(),
            mesh.Submeshes.Select(ToInternal).ToArray());
    }

    private static ResoniteMeshVertex ToInternal(MeshVertex vertex)
    {
        return new ResoniteMeshVertex(
            ToInternal(vertex.Position),
            ToInternal(vertex.Normal),
            ToInternal(vertex.UV0),
            vertex.Color is null ? null : ToInternal(vertex.Color));
    }

    private static ResoniteMeshSubmesh ToInternal(MeshSubmesh submesh)
    {
        return new ResoniteMeshSubmesh(submesh.Index, submesh.MaterialKey, submesh.TriangleVertexIndices);
    }

    internal static ResoniteMaterialBinding ToInternal(MaterialBinding binding)
    {
        return new ResoniteMaterialBinding(
            binding.MaterialKey,
            ToInternal(binding.BaseColor),
            (ResoniteMaterialType)binding.MaterialType,
            binding.TexturePayload is null ? null : ToInternal(binding.TexturePayload),
            (ResoniteTextureSourceKind)binding.TextureSourceKind,
            (ResoniteMaterialProjection)binding.Projection,
            binding.DepthOffset is null ? null : ToInternal(binding.DepthOffset),
            binding.SubmeshIndices,
            binding.TextureScale is null ? null : ToInternal(binding.TextureScale),
            binding.Family,
            binding.TextureOffset is null ? null : ToInternal(binding.TextureOffset),
            binding.ReuseScope == MaterialReuseScope.Shared ? ResoniteMaterialAssetScope.Common : ResoniteMaterialAssetScope.PresentationSlotScoped,
            binding.TerrainOverlay,
            binding.BundledVariantIndex);
    }

    private static ResoniteTexturePayload ToInternal(TexturePayload payload)
    {
        return new ResoniteTexturePayload(
            payload.Width,
            payload.Height,
            payload.ColorProfile,
            payload.BinaryPayload.ToArray(),
            payload.Identity,
            (ResoniteTexturePayloadFormat)payload.Format);
    }

    private static ResoniteMaterialDepthOffset ToInternal(MaterialDepthOffset value) => new(value.Factor, value.Units);
}
