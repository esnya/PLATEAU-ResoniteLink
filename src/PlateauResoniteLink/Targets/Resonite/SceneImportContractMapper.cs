using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class SceneImportContractMapper
{
    internal static ImportedCityObject[] ToContract(IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);
        return cityObjects.Select(ToContract).ToArray();
    }

    internal static MaterialBinding[] ToContract(IReadOnlyList<ResoniteMaterialBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return bindings.Select(ToContract).ToArray();
    }

    internal static ImportedSceneMetadata ToContract(ResoniteConstructionMetadata metadata)
    {
        return new ImportedSceneMetadata(
            metadata.SchemaVersion,
            metadata.WorldName,
            metadata.Request,
            metadata.SourceDataset,
            ToContract(metadata.Attribution),
            ToContract(metadata.LocalOrigin));
    }

    internal static ImportedCityObject ToContract(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => new ImportedCityObject(
                cityObject.SlotKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToContract(cityObject.Transform),
                ToContract(triangleMesh.Mesh),
                cityObject.Materials.Select(ToContract).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceObjectKey,
                cityObject.SourceUnitKey,
                cityObject.SourceFileRelativePath),
            ResoniteHeightMapGridGeometry heightMap => new ImportedCityObject(
                cityObject.SlotKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToContract(cityObject.Transform),
                new HeightMapGridGeometry(
                    heightMap.Width,
                    heightMap.Height,
                    ToContract(heightMap.Size),
                    heightMap.MinHeight,
                    heightMap.MaxHeight,
                    heightMap.HeightSamples,
                    heightMap.UvScale is null ? null : ToContract(heightMap.UvScale),
                    heightMap.UvOffset is null ? null : ToContract(heightMap.UvOffset)),
                cityObject.Materials.Select(ToContract).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceObjectKey,
                cityObject.SourceUnitKey,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    internal static ResoniteConstructionMetadata ToInternal(ImportedSceneMetadata metadata)
    {
        return new ResoniteConstructionMetadata(
            metadata.SchemaVersion,
            metadata.SceneName,
            metadata.Request,
            metadata.SourceDataset,
            ToInternal(metadata.Attribution),
            ToInternal(metadata.GeodeticOrigin));
    }

    internal static ResoniteConstructionCityObject[] ToInternal(IReadOnlyList<ImportedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);
        return cityObjects.Select(ToInternal).ToArray();
    }

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
                cityObject.SourceObjectKey,
                cityObject.SourceUnitKey,
                cityObject.SourceFileRelativePath),
            HeightMapGridGeometry heightMap => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                new ResoniteHeightMapGridGeometry(
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
                cityObject.SourceObjectKey,
                cityObject.SourceUnitKey,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    private static Attribution ToContract(ResoniteAttribution attribution)
    {
        return new Attribution(
            ToContract(attribution.DatasetLicense),
            attribution.MaterialLicenses.Select(ToContract).ToArray());
    }

    private static MaterialAttribution ToContract(ResoniteMaterialAttribution attribution)
    {
        return new MaterialAttribution(
            attribution.MaterialKey,
            attribution.License is null ? null : ToContract(attribution.License));
    }

    private static LicenseMetadata ToContract(LicenseAttributionMetadata metadata)
    {
        return new LicenseMetadata(
            metadata.RequireCredit,
            metadata.CreditText,
            metadata.LicenseName,
            metadata.LicenseUrl);
    }

    private static GeodeticOrigin ToContract(ResoniteLocalOrigin origin)
    {
        return new GeodeticOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
    }

    private static Transform3D ToContract(ResoniteTransform transform)
    {
        return new Transform3D(
            ToContract(transform.Position),
            transform.Rotation is null ? null : ToContract(transform.Rotation));
    }

    private static Float2 ToContract(ResoniteFloat2 value) => new(value.X, value.Y);

    private static Float3 ToContract(ResoniteFloat3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToContract(ResoniteFloatQ value) => new(value.X, value.Y, value.Z, value.W);

    private static ColorRgba ToContract(ResoniteColor value) => new(value.R, value.G, value.B, value.A);

    private static ImportedMesh ToContract(ResoniteImportedMesh mesh)
    {
        return new ImportedMesh(
            mesh.Vertices.Select(ToContract).ToArray(),
            mesh.Submeshes.Select(ToContract).ToArray());
    }

    private static MeshVertex ToContract(ResoniteMeshVertex vertex)
    {
        return new MeshVertex(
            ToContract(vertex.Position),
            ToContract(vertex.Normal),
            ToContract(vertex.UV0),
            vertex.Color is null ? null : ToContract(vertex.Color));
    }

    private static MeshSubmesh ToContract(ResoniteMeshSubmesh submesh)
    {
        return new MeshSubmesh(submesh.Index, submesh.MaterialKey, submesh.TriangleVertexIndices);
    }

    internal static MaterialBinding ToContract(ResoniteMaterialBinding binding)
    {
        return new MaterialBinding(
            binding.MaterialKey,
            ToContract(binding.BaseColor),
            (MaterialType)binding.MaterialType,
            binding.TexturePayload is null ? null : ToContract(binding.TexturePayload),
            (TextureSourceKind)binding.TextureSourceKind,
            (MaterialProjection)binding.Projection,
            binding.DepthOffset is null ? null : ToContract(binding.DepthOffset),
            binding.SubmeshIndices,
            binding.TextureScale is null ? null : ToContract(binding.TextureScale),
            binding.Family,
            binding.TextureOffset is null ? null : ToContract(binding.TextureOffset),
            binding.AssetScope == ResoniteMaterialAssetScope.Common ? MaterialReuseScope.Shared : MaterialReuseScope.PerObject,
            binding.TerrainOverlay,
            binding.BundledVariantIndex);
    }

    private static TexturePayload ToContract(ResoniteTexturePayload payload)
    {
        return new TexturePayload(
            payload.Width,
            payload.Height,
            payload.ColorProfile,
            payload.BinaryPayload,
            payload.Identity,
            (TexturePayloadFormat)payload.Format);
    }

    private static MaterialDepthOffset ToContract(ResoniteMaterialDepthOffset value) => new(value.Factor, value.Units);

    private static ResoniteAttribution ToInternal(Attribution attribution)
    {
        return new ResoniteAttribution(
            ToInternal(attribution.DatasetLicense),
            attribution.MaterialLicenses.Select(ToInternal).ToArray());
    }

    private static ResoniteMaterialAttribution ToInternal(MaterialAttribution attribution)
    {
        return new ResoniteMaterialAttribution(
            attribution.MaterialKey,
            attribution.License is null ? null : ToInternal(attribution.License));
    }

    private static LicenseAttributionMetadata ToInternal(LicenseMetadata metadata)
    {
        return new LicenseAttributionMetadata(
            metadata.RequireCredit,
            metadata.CreditText,
            metadata.LicenseName,
            metadata.LicenseUrl);
    }

    private static ResoniteLocalOrigin ToInternal(GeodeticOrigin origin)
    {
        return new ResoniteLocalOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
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
            payload.BinaryPayload,
            payload.Identity,
            (ResoniteTexturePayloadFormat)payload.Format);
    }

    private static ResoniteMaterialDepthOffset ToInternal(MaterialDepthOffset value) => new(value.Factor, value.Units);
}
