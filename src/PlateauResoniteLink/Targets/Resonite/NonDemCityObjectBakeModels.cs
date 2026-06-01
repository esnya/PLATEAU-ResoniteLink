using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record NonDemMaterialAtlasTile(Image<Rgba32> Image, Rgba32 BackgroundColor);

internal sealed record NonDemRenderedAtlas(
    NonDemAtlasLayout<NonDemAtlasBatchEntry> Layout,
    Image<Rgba32> Image) : IDisposable
{
    public void Dispose()
    {
        Image.Dispose();
    }
}

internal sealed record NonDemAtlasOrPreservedEntry(
    NonDemAtlasBatchEntry? AtlasEntry,
    NonDemPreservedSubmeshEntry? PreservedEntry);

internal sealed record NonDemSourceScopedTriangleCityObject(
    ResoniteConstructionCityObject CityObject,
    ResoniteImportedMesh Mesh,
    string SourceFileRelativePath)
{
    public string SlotKey => CityObject.SlotKey;

    public string DisplayName => CityObject.DisplayName;

    public string PackageName => CityObject.PackageName;

    public string ActualMeshCode => CityObject.ActualMeshCode;

    public int? LodLevel => CityObject.LodLevel;

    public ResoniteTransform Transform => CityObject.Transform;

    public IReadOnlyList<ResoniteMaterialBinding> Materials => CityObject.Materials;

    public bool CollisionEnabled => CityObject.CollisionEnabled;

    public static bool TryCreate(
        ResoniteConstructionCityObject cityObject,
        [NotNullWhen(true)] out NonDemSourceScopedTriangleCityObject? scopedCityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (cityObject.Geometry is not ResoniteTriangleMeshGeometry triangleMesh
            || string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            scopedCityObject = null;
            return false;
        }

        scopedCityObject = new NonDemSourceScopedTriangleCityObject(
            cityObject,
            triangleMesh.Mesh,
            cityObject.SourceFileRelativePath);
        return true;
    }

    public NonDemSourceScopedTriangleCityObject WithMeshAndMaterials(
        ResoniteImportedMesh mesh,
        IReadOnlyList<ResoniteMaterialBinding> materials)
    {
        return new NonDemSourceScopedTriangleCityObject(
            CityObject with
            {
                Geometry = new ResoniteTriangleMeshGeometry(mesh),
                Materials = materials,
            },
            mesh,
            SourceFileRelativePath);
    }
}

internal readonly record struct NonDemBufferedCityObject(
    NonDemSourceScopedTriangleCityObject CityObject,
    NonDemCityObjectBakePolicy Policy);

internal sealed record NonDemAtlasBatchEntry(
    NonDemSourceScopedTriangleCityObject CityObject,
    ResoniteMeshSubmesh Submesh,
    ResoniteMaterialBinding Material,
    NonDemMaterialAtlasTile Tile,
    TextureUvRect UvBounds);

internal sealed record NonDemPreservedSubmeshEntry(
    NonDemSourceScopedTriangleCityObject CityObject,
    ResoniteMeshSubmesh Submesh,
    ResoniteMaterialBinding Material,
    ResoniteColor? VertexColorOverride = null);

internal sealed record NonDemOrderedPreservedSubmeshEntry(
    NonDemPreservedSubmeshEntry Entry,
    int Order);

internal sealed record NonDemCityObjectBakeCandidate(
    NonDemSourceScopedTriangleCityObject CityObject,
    IReadOnlyList<NonDemAtlasBatchEntry> AtlasEntries,
    IReadOnlyList<NonDemPreservedSubmeshEntry> PreservedEntries);
