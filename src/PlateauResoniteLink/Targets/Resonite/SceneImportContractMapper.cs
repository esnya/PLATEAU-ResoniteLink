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
            DynamicTerrainGeometry dynamicTerrain => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                new ResoniteDynamicTerrainGeometry(
                    new ResoniteTriangleMeshGeometry(ToInternal(dynamicTerrain.StaticMesh.Mesh)),
                    new ResoniteTerrainGridGeometry(
                        dynamicTerrain.GridMesh.Width,
                        dynamicTerrain.GridMesh.Height,
                        ToInternal(dynamicTerrain.GridMesh.Size),
                        dynamicTerrain.GridMesh.MinHeight,
                        dynamicTerrain.GridMesh.MaxHeight,
                        dynamicTerrain.GridMesh.HeightSamples,
                        dynamicTerrain.GridMesh.UvScale is null ? null : ToInternal(dynamicTerrain.GridMesh.UvScale),
                        dynamicTerrain.GridMesh.UvOffset is null ? null : ToInternal(dynamicTerrain.GridMesh.UvOffset))),
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
        return new ResoniteMeshSubmesh(submesh.Index, submesh.TriangleVertexIndices);
    }

    internal static ResoniteMaterialBinding ToInternal(MaterialBinding binding)
    {
        return new ResoniteMaterialBinding(
            BaseColor: ToInternal(binding.BaseColor),
            MaterialType: ToInternal(binding.MaterialType),
            TexturePayload: binding.TexturePayload is null ? null : ToInternal(binding.TexturePayload),
            TextureSourceKind: ToInternal(binding.TextureSourceKind),
            Projection: ToInternal(binding.Projection),
            DepthOffset: binding.DepthOffset is null ? null : ToInternal(binding.DepthOffset),
            SubmeshIndices: binding.SubmeshIndices,
            AssetBinding: ToInternalAssetBinding(binding),
            TextureScale: binding.TextureScale is null ? null : ToInternal(binding.TextureScale),
            Family: binding.Family,
            TextureOffset: binding.TextureOffset is null ? null : ToInternal(binding.TextureOffset),
            TerrainOverlayMaterial: binding.TerrainOverlayMaterial,
            BundledVariantIndex: binding.BundledVariantIndex);
    }

    private static ResoniteMaterialAssetBinding ToInternalAssetBinding(MaterialBinding binding)
    {
        return binding switch
        {
            SharedCommonMaterialBinding { CommonMaterial: { } commonMaterial } =>
                ResoniteMaterialAssetBinding.SharedCommon(commonMaterial),
            PresentationCommonMaterialBinding { CommonMaterial: { } commonMaterial } =>
                ResoniteMaterialAssetBinding.PresentationCommon(commonMaterial),
            _ when binding.ReuseScope == MaterialReuseScope.Shared
                && binding.CommonMaterial is { } commonMaterial
                && (binding.TerrainOverlayMaterial is null || binding.CommonMaterial is not null) =>
                ResoniteMaterialAssetBinding.SharedCommon(commonMaterial),
            _ when binding.ReuseScope == MaterialReuseScope.Shared
                && binding.TerrainOverlayMaterial is null =>
                ResoniteMaterialAssetBinding.Shared,
            _ => binding.CommonMaterial is { } commonMaterial
                ? ResoniteMaterialAssetBinding.PresentationCommon(commonMaterial)
                : ResoniteMaterialAssetBinding.Presentation,
        };
    }

    private static ResoniteTexturePayload ToInternal(TexturePayload payload)
    {
        return payload switch
        {
            RawRgba32TexturePayload raw => new RawRgba32ResoniteTexturePayload(
                raw.Width,
                raw.Height,
                raw.ColorProfile,
                raw.BinaryPayload.AsSpan().ToArray(),
                raw.Identity),
            EncodedImageTexturePayload encoded => new EncodedImageResoniteTexturePayload(
                encoded.Width,
                encoded.Height,
                encoded.ColorProfile,
                encoded.Source,
                encoded.Identity),
            _ => throw new ArgumentOutOfRangeException(nameof(payload), payload.GetType(), "Unsupported texture payload type."),
        };
    }

    private static ResoniteMaterialType ToInternal(MaterialType materialType)
    {
        return materialType switch
        {
            MaterialType.Standard => ResoniteMaterialType.Standard,
            MaterialType.Wireframe => ResoniteMaterialType.Wireframe,
            MaterialType.VertexColor => ResoniteMaterialType.VertexColor,
            _ => throw new ArgumentOutOfRangeException(nameof(materialType), materialType, "Unsupported material type."),
        };
    }

    private static ResoniteTextureSourceKind ToInternal(TextureSourceKind sourceKind)
    {
        return sourceKind switch
        {
            TextureSourceKind.Dataset => ResoniteTextureSourceKind.Dataset,
            TextureSourceKind.Bundled => ResoniteTextureSourceKind.Bundled,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unsupported texture source kind."),
        };
    }

    private static ResoniteMaterialProjection ToInternal(MaterialProjection projection)
    {
        return projection switch
        {
            MaterialProjection.Uv => ResoniteMaterialProjection.Uv,
            MaterialProjection.Triplanar => ResoniteMaterialProjection.Triplanar,
            _ => throw new ArgumentOutOfRangeException(nameof(projection), projection, "Unsupported material projection."),
        };
    }

    private static ResoniteMaterialDepthOffset ToInternal(MaterialDepthOffset value) => new(value.Factor, value.Units);
}
