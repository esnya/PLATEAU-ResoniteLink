using InternalModel = Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class SceneImportContractMapper
{
    public static ImportedSceneMetadata ToContract(InternalModel.ResoniteConstructionMetadata metadata)
    {
        return new ImportedSceneMetadata(
            metadata.SchemaVersion,
            metadata.WorldName,
            metadata.Request,
            metadata.SourceDataset,
            ToContract(metadata.Attribution),
            ToContract(metadata.LocalOrigin));
    }

    public static ImportedCityObject ToContract(InternalModel.ResoniteConstructionCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            InternalModel.ResoniteTriangleMeshGeometry triangleMesh => new ImportedCityObject(
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
            InternalModel.ResoniteHeightMapGridGeometry heightMap => new ImportedCityObject(
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
                    heightMap.HeightSamples),
                cityObject.Materials.Select(ToContract).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceObjectKey,
                cityObject.SourceUnitKey,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    public static InternalModel.ResoniteConstructionMetadata ToInternal(ImportedSceneMetadata metadata)
    {
        return new InternalModel.ResoniteConstructionMetadata(
            metadata.SchemaVersion,
            metadata.SceneName,
            metadata.Request,
            metadata.SourceDataset,
            ToInternal(metadata.Attribution),
            ToInternal(metadata.GeodeticOrigin));
    }

    public static InternalModel.ResoniteConstructionCityObject ToInternal(ImportedCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            TriangleMeshGeometry triangleMesh => new InternalModel.ResoniteConstructionCityObject(
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
            HeightMapGridGeometry heightMap => new InternalModel.ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                new InternalModel.ResoniteHeightMapGridGeometry(
                    heightMap.Width,
                    heightMap.Height,
                    ToInternal(heightMap.Size),
                    heightMap.MinHeight,
                    heightMap.MaxHeight,
                    heightMap.HeightSamples),
                cityObject.Materials.Select(ToInternal).ToArray(),
                cityObject.CollisionEnabled,
                cityObject.SourceObjectKey,
                cityObject.SourceUnitKey,
                cityObject.SourceFileRelativePath),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    private static Attribution ToContract(InternalModel.ResoniteAttribution attribution)
    {
        return new Attribution(
            ToContract(attribution.DatasetLicense),
            attribution.MaterialLicenses.Select(ToContract).ToArray());
    }

    private static MaterialAttribution ToContract(InternalModel.ResoniteMaterialAttribution attribution)
    {
        return new MaterialAttribution(
            attribution.MaterialKey,
            attribution.License is null ? null : ToContract(attribution.License));
    }

    private static LicenseMetadata ToContract(InternalModel.LicenseAttributionMetadata metadata)
    {
        return new LicenseMetadata(
            metadata.RequireCredit,
            metadata.CreditText,
            metadata.LicenseName,
            metadata.LicenseUrl);
    }

    private static GeodeticOrigin ToContract(InternalModel.ResoniteLocalOrigin origin)
    {
        return new GeodeticOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
    }

    private static Transform3d ToContract(InternalModel.ResoniteTransform transform)
    {
        return new Transform3d(
            ToContract(transform.Position),
            transform.Rotation is null ? null : ToContract(transform.Rotation));
    }

    private static Float2 ToContract(InternalModel.ResoniteFloat2 value) => new(value.X, value.Y);

    private static Float3 ToContract(InternalModel.ResoniteFloat3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToContract(InternalModel.ResoniteFloatQ value) => new(value.X, value.Y, value.Z, value.W);

    private static ColorRgba ToContract(InternalModel.ResoniteColor value) => new(value.R, value.G, value.B, value.A);

    private static ImportedMesh ToContract(InternalModel.ResoniteImportedMesh mesh)
    {
        return new ImportedMesh(
            mesh.Vertices.Select(ToContract).ToArray(),
            mesh.Submeshes.Select(ToContract).ToArray());
    }

    private static MeshVertex ToContract(InternalModel.ResoniteMeshVertex vertex)
    {
        return new MeshVertex(
            ToContract(vertex.Position),
            ToContract(vertex.Normal),
            ToContract(vertex.UV0),
            vertex.Color is null ? null : ToContract(vertex.Color));
    }

    private static MeshSubmesh ToContract(InternalModel.ResoniteMeshSubmesh submesh)
    {
        return new MeshSubmesh(submesh.Index, submesh.MaterialKey, submesh.TriangleVertexIndices);
    }

    internal static MaterialBinding ToContract(InternalModel.ResoniteMaterialBinding binding)
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
            binding.AssetScope == InternalModel.ResoniteMaterialAssetScope.Common ? MaterialReuseScope.Shared : MaterialReuseScope.PerObject,
            binding.TerrainOverlay,
            binding.BundledVariantIndex);
    }

    private static TexturePayload ToContract(InternalModel.ResoniteTexturePayload payload)
    {
        return new TexturePayload(
            payload.Width,
            payload.Height,
            payload.ColorProfile,
            payload.BinaryPayload,
            payload.Identity,
            (TexturePayloadFormat)payload.Format);
    }

    private static MaterialDepthOffset ToContract(InternalModel.ResoniteMaterialDepthOffset value) => new(value.Factor, value.Units);

    private static InternalModel.ResoniteAttribution ToInternal(Attribution attribution)
    {
        return new InternalModel.ResoniteAttribution(
            ToInternal(attribution.DatasetLicense),
            attribution.MaterialLicenses.Select(ToInternal).ToArray());
    }

    private static InternalModel.ResoniteMaterialAttribution ToInternal(MaterialAttribution attribution)
    {
        return new InternalModel.ResoniteMaterialAttribution(
            attribution.MaterialKey,
            attribution.License is null ? null : ToInternal(attribution.License));
    }

    private static InternalModel.LicenseAttributionMetadata ToInternal(LicenseMetadata metadata)
    {
        return new InternalModel.LicenseAttributionMetadata(
            metadata.RequireCredit,
            metadata.CreditText,
            metadata.LicenseName,
            metadata.LicenseUrl);
    }

    private static InternalModel.ResoniteLocalOrigin ToInternal(GeodeticOrigin origin)
    {
        return new InternalModel.ResoniteLocalOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
    }

    private static InternalModel.ResoniteTransform ToInternal(Transform3d transform)
    {
        return new InternalModel.ResoniteTransform(
            ToInternal(transform.Position),
            transform.Rotation is null ? null : ToInternal(transform.Rotation));
    }

    private static InternalModel.ResoniteFloat2 ToInternal(Float2 value) => new(value.X, value.Y);

    private static InternalModel.ResoniteFloat3 ToInternal(Float3 value) => new(value.X, value.Y, value.Z);

    private static InternalModel.ResoniteFloatQ ToInternal(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static InternalModel.ResoniteColor ToInternal(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static InternalModel.ResoniteImportedMesh ToInternal(ImportedMesh mesh)
    {
        return new InternalModel.ResoniteImportedMesh(
            mesh.Vertices.Select(ToInternal).ToArray(),
            mesh.Submeshes.Select(ToInternal).ToArray());
    }

    private static InternalModel.ResoniteMeshVertex ToInternal(MeshVertex vertex)
    {
        return new InternalModel.ResoniteMeshVertex(
            ToInternal(vertex.Position),
            ToInternal(vertex.Normal),
            ToInternal(vertex.UV0),
            vertex.Color is null ? null : ToInternal(vertex.Color));
    }

    private static InternalModel.ResoniteMeshSubmesh ToInternal(MeshSubmesh submesh)
    {
        return new InternalModel.ResoniteMeshSubmesh(submesh.Index, submesh.MaterialKey, submesh.TriangleVertexIndices);
    }

    private static InternalModel.ResoniteMaterialBinding ToInternal(MaterialBinding binding)
    {
        return new InternalModel.ResoniteMaterialBinding(
            binding.MaterialKey,
            ToInternal(binding.BaseColor),
            (InternalModel.ResoniteMaterialType)binding.MaterialType,
            binding.TexturePayload is null ? null : ToInternal(binding.TexturePayload),
            (InternalModel.ResoniteTextureSourceKind)binding.TextureSourceKind,
            (InternalModel.ResoniteMaterialProjection)binding.Projection,
            binding.DepthOffset is null ? null : ToInternal(binding.DepthOffset),
            binding.SubmeshIndices,
            binding.TextureScale is null ? null : ToInternal(binding.TextureScale),
            binding.Family,
            binding.TextureOffset is null ? null : ToInternal(binding.TextureOffset),
            binding.ReuseScope == MaterialReuseScope.Shared ? InternalModel.ResoniteMaterialAssetScope.Common : InternalModel.ResoniteMaterialAssetScope.PresentationSlotScoped,
            binding.TerrainOverlay,
            binding.BundledVariantIndex);
    }

    private static InternalModel.ResoniteTexturePayload ToInternal(TexturePayload payload)
    {
        return new InternalModel.ResoniteTexturePayload(
            payload.Width,
            payload.Height,
            payload.ColorProfile,
            payload.BinaryPayload,
            payload.Identity,
            (InternalModel.ResoniteTexturePayloadFormat)payload.Format);
    }

    private static InternalModel.ResoniteMaterialDepthOffset ToInternal(MaterialDepthOffset value) => new(value.Factor, value.Units);
}
