using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record NonDemMaterialAtlasTile(Image<Rgba32> Image, Rgba32 BackgroundColor);

internal sealed record NonDemAtlasBakeMaterial
{
    private NonDemAtlasBakeMaterial(ResoniteMaterialBinding material, ResoniteTexturePayload texturePayload)
    {
        Material = material;
        TexturePayload = texturePayload;
    }

    public ResoniteMaterialBinding Material { get; }

    public ResoniteTexturePayload TexturePayload { get; }

    public static NonDemAtlasBakeMaterial? TryCreate(ResoniteMaterialBinding material)
    {
        if (material.DepthOffset is not null
            || material.Projection != ResoniteMaterialProjection.Uv
            || material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            return null;
        }

        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.TexturePayload is null
            || material.TextureSourceKind != ResoniteTextureSourceKind.Dataset)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(material.Family)
            && (material.TerrainOverlay is not null
                || !ResoniteMaterialSharing.CanUseSharedAlbedoOnlyMaterial(material)))
        {
            return null;
        }

        return new NonDemAtlasBakeMaterial(material, material.TexturePayload);
    }
}

internal abstract record NonDemMaterialBakeClassification
{
    private NonDemMaterialBakeClassification()
    {
    }

    public sealed record AtlasCandidate(NonDemAtlasBakeMaterial Candidate) : NonDemMaterialBakeClassification;

    public sealed record Preserved(NonDemPreservedMaterialKind Kind) : NonDemMaterialBakeClassification;
}

internal abstract record NonDemAtlasOrPreservedEntry
{
    private NonDemAtlasOrPreservedEntry()
    {
    }

    public sealed record Atlas(NonDemAtlasBatchEntry Entry) : NonDemAtlasOrPreservedEntry;

    public sealed record Preserved(NonDemPreservedSubmeshEntry Entry) : NonDemAtlasOrPreservedEntry;
}

internal readonly record struct NonDemBufferedCityObject(
    ResoniteConstructionCityObject CityObject,
    NonDemCityObjectBakePolicy Policy);

internal sealed record NonDemAtlasBatchEntry(
    ResoniteConstructionCityObject CityObject,
    ResoniteMeshSubmesh Submesh,
    ResoniteMaterialBinding Material,
    NonDemMaterialAtlasTile Tile,
    TextureUvRect UvBounds);

internal sealed record NonDemPreservedSubmeshEntry(
    ResoniteConstructionCityObject CityObject,
    ResoniteMeshSubmesh Submesh,
    ResoniteMaterialBinding Material,
    ResoniteColor? VertexColorOverride = null);

internal sealed record NonDemOrderedPreservedSubmeshEntry(
    NonDemPreservedSubmeshEntry Entry,
    int Order);

internal sealed record NonDemCityObjectBakeCandidate(
    ResoniteConstructionCityObject CityObject,
    IReadOnlyList<NonDemAtlasBatchEntry> AtlasEntries,
    IReadOnlyList<NonDemPreservedSubmeshEntry> PreservedEntries);
