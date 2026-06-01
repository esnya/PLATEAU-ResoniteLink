using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemAtlasOrPreservedEntryFactory
{
    Task<NonDemAtlasOrPreservedEntry> CreateAsync(
        ResoniteConstructionCityObject cityObject,
        ResoniteMeshSubmesh submesh,
        NonDemAtlasBakeMaterial atlasMaterial,
        CancellationToken cancellationToken);
}

internal sealed class NonDemAtlasOrPreservedEntryFactory(
    ResoniteTextureImageLoader textureImageLoader,
    int maxAtlasTextureEdge) : INonDemAtlasOrPreservedEntryFactory
{
    private readonly ResoniteTextureImageLoader textureImageLoader = textureImageLoader
        ?? throw new ArgumentNullException(nameof(textureImageLoader));

    public async Task<NonDemAtlasOrPreservedEntry> CreateAsync(
        ResoniteConstructionCityObject cityObject,
        ResoniteMeshSubmesh submesh,
        NonDemAtlasBakeMaterial atlasMaterial,
        CancellationToken cancellationToken)
    {
        ResoniteMaterialBinding material = atlasMaterial.Material;
        TextureUvRect uvBounds = ComputeUvBounds(cityObject.Mesh.Vertices, submesh);
        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            ResoniteTextureImportFactory.CreateSourceFromPayload(atlasMaterial.TexturePayload),
            cancellationToken);
        Rgba32 detectedBackgroundColor = NonDemTextureImageProcessing.DetectRepresentativeBackgroundColor(sourceImage);
        using Image<Rgba32> preparedSourceImage = sourceImage.Clone();
        NonDemTextureImageProcessing.FillTransparentRgb(preparedSourceImage, detectedBackgroundColor);
        if (NonDemTextureImageProcessing.TryGetUniformPixelColor(preparedSourceImage, out Rgba32 uniformDatasetColor))
        {
            Rgba32 tintedUniformColor = NonDemTextureImageProcessing.MultiplyPixel(
                uniformDatasetColor,
                NonDemTextureImageProcessing.ToPixel(material.BaseColor));
            return new NonDemAtlasOrPreservedEntry.Preserved(
                new NonDemPreservedSubmeshEntry(
                    cityObject,
                    submesh,
                    CreateVertexColorMaterial(material, submesh.Index),
                    NonDemTextureImageProcessing.ToColor(tintedUniformColor)));
        }

        int targetWidth = Math.Max(1, Math.Min(maxAtlasTextureEdge, (int)Math.Ceiling(sourceImage.Width * uvBounds.Width)));
        int targetHeight = Math.Max(1, Math.Min(maxAtlasTextureEdge, (int)Math.Ceiling(sourceImage.Height * uvBounds.Height)));
        using Image<Rgba32> bakedImage = NonDemTextureImageProcessing.BakeUsedUvRegion(
            preparedSourceImage,
            uvBounds,
            targetWidth,
            targetHeight);

        NonDemTextureImageProcessing.ApplyBaseColor(bakedImage, material.BaseColor);
        return new NonDemAtlasOrPreservedEntry.Atlas(
            new NonDemAtlasBatchEntry(
                cityObject,
                submesh,
                material,
                new NonDemMaterialAtlasTile(
                    bakedImage.Clone(),
                    NonDemTextureImageProcessing.MultiplyPixel(
                        detectedBackgroundColor,
                        NonDemTextureImageProcessing.ToPixel(material.BaseColor))),
                uvBounds));
    }

    private static ResoniteMaterialBinding CreateVertexColorMaterial(ResoniteMaterialBinding material, int submeshIndex)
    {
        return material with
        {
            MaterialType = ResoniteMaterialType.VertexColor,
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload = null,
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            TextureScale = null,
            TextureOffset = null,
            Family = null,
            TerrainOverlayMaterial = null,
            AssetScope = ResoniteMaterialAssetScope.Common,
            SubmeshIndices = [submeshIndex],
            CommonMaterial = material.DepthOffset is null
                ? CommonMaterialCatalog.Create().VertexColor.Uv
                : CommonMaterialCatalog.Create().VertexColor.TerrainAlignedUv,
        };
    }

    private static TextureUvRect ComputeUvBounds(
        IReadOnlyList<ResoniteMeshVertex> vertices,
        ResoniteMeshSubmesh submesh)
    {
        double minU = double.PositiveInfinity;
        double minV = double.PositiveInfinity;
        double maxU = double.NegativeInfinity;
        double maxV = double.NegativeInfinity;

        foreach (int sourceIndex in submesh.TriangleVertexIndices)
        {
            ResoniteFloat2 transformedUv = vertices[sourceIndex].UV0;
            minU = Math.Min(minU, transformedUv.X);
            minV = Math.Min(minV, transformedUv.Y);
            maxU = Math.Max(maxU, transformedUv.X);
            maxV = Math.Max(maxV, transformedUv.Y);
        }

        if (double.IsPositiveInfinity(minU) || double.IsPositiveInfinity(minV))
        {
            return TextureUvRect.Identity;
        }

        double width = Math.Max(1.0 / 1024.0, maxU - minU);
        double height = Math.Max(1.0 / 1024.0, maxV - minV);
        return new TextureUvRect(minU, minV, width, height);
    }
}
