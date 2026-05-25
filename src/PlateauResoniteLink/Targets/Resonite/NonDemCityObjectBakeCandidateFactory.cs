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

internal sealed class NonDemCityObjectBakeCandidateFactory(
    ResoniteTextureImageLoader textureImageLoader,
    int maxAtlasTextureEdge)
{
    public async Task<NonDemCityObjectBakeCandidate?> CreateAsync(
        NonDemBufferedCityObject bufferedCityObject,
        CancellationToken cancellationToken)
    {
        ResoniteConstructionCityObject cityObject = bufferedCityObject.CityObject;
        NonDemCityObjectBakePolicy policy = bufferedCityObject.Policy;
        if (!NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(cityObject, out _))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        ResoniteConstructionCityObject normalizedCityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        if (!NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(normalizedCityObject, out Dictionary<int, ResoniteMaterialBinding>? materialBySubmeshIndex))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        List<NonDemAtlasBatchEntry> atlasEntries = [];
        List<NonDemPreservedSubmeshEntry> preservedEntries = [];
        bool hadAtlasCandidateMaterial = false;
        foreach (ResoniteMeshSubmesh submesh in normalizedCityObject.Mesh.Submeshes.OrderBy(static candidate => candidate.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
            {
                throw new InvalidOperationException(
                    $"Non-DEM bake city object '{cityObject.DisplayName}' left submesh index {submesh.Index} without a material assignment.");
            }

            NonDemMaterialBakeCategory category = NonDemCityObjectBakeMaterialClassifier.Classify(material);
            switch (category)
            {
                case NonDemMaterialBakeCategory.AtlasCandidate:
                    hadAtlasCandidateMaterial = true;
                    NonDemAtlasOrPreservedEntry bakeEntry = await CreateAtlasOrPreservedEntryAsync(
                        normalizedCityObject,
                        submesh,
                        material,
                        cancellationToken);
                    if (bakeEntry.AtlasEntry is not null)
                    {
                        atlasEntries.Add(bakeEntry.AtlasEntry);
                    }

                    if (bakeEntry.PreservedEntry is not null)
                    {
                        preservedEntries.Add(bakeEntry.PreservedEntry);
                    }

                    break;
                case NonDemMaterialBakeCategory.PreservedCommonMaterial when policy.PreserveCommonMaterials:
                case NonDemMaterialBakeCategory.PreservedTextureless when policy.PreserveTexturelessMaterials:
                case NonDemMaterialBakeCategory.PreservedVertexColor when policy.PreserveVertexColorMaterials:
                case NonDemMaterialBakeCategory.PreservedOther:
                    ResoniteMeshSubmesh normalizedSubmesh = normalizedCityObject.Mesh.Submeshes.Single(candidate => candidate.Index == submesh.Index);
                    ResoniteMaterialBinding normalizedMaterial = normalizedCityObject.Materials.Single(candidate => candidate.SubmeshIndices.Contains(submesh.Index));
                    preservedEntries.Add(new NonDemPreservedSubmeshEntry(normalizedCityObject, normalizedSubmesh, normalizedMaterial));
                    break;
            }
        }

        if (policy.RequireAtlasCandidateMaterial && !hadAtlasCandidateMaterial)
        {
            return null;
        }

        if (atlasEntries.Count == 0 && preservedEntries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' produced no atlas or preserved submesh candidate.");
        }

        return new NonDemCityObjectBakeCandidate(normalizedCityObject, atlasEntries, preservedEntries);
    }

    private async Task<NonDemAtlasOrPreservedEntry> CreateAtlasOrPreservedEntryAsync(
        ResoniteConstructionCityObject cityObject,
        ResoniteMeshSubmesh submesh,
        ResoniteMaterialBinding material,
        CancellationToken cancellationToken)
    {
        if (material.TexturePayload is null)
        {
            throw new InvalidOperationException("Non-DEM bake candidate material must have a texture payload.");
        }

        TextureUvRect uvBounds = ComputeUvBounds(cityObject.Mesh.Vertices, submesh);
        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
            cancellationToken);
        Rgba32 detectedBackgroundColor = NonDemTextureImageProcessing.DetectRepresentativeBackgroundColor(sourceImage);
        using Image<Rgba32> preparedSourceImage = sourceImage.Clone();
        NonDemTextureImageProcessing.FillTransparentRgb(preparedSourceImage, detectedBackgroundColor);
        if (NonDemTextureImageProcessing.TryGetUniformPixelColor(preparedSourceImage, out Rgba32 uniformDatasetColor))
        {
            Rgba32 tintedUniformColor = NonDemTextureImageProcessing.MultiplyPixel(
                uniformDatasetColor,
                NonDemTextureImageProcessing.ToPixel(material.BaseColor));
            return new NonDemAtlasOrPreservedEntry(
                AtlasEntry: null,
                PreservedEntry: new NonDemPreservedSubmeshEntry(
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
        return new NonDemAtlasOrPreservedEntry(
            AtlasEntry: new NonDemAtlasBatchEntry(
                cityObject,
                submesh,
                material,
                new NonDemMaterialAtlasTile(
                    bakedImage.Clone(),
                    NonDemTextureImageProcessing.MultiplyPixel(
                        detectedBackgroundColor,
                        NonDemTextureImageProcessing.ToPixel(material.BaseColor))),
                uvBounds),
            PreservedEntry: null);
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
            TerrainOverlay = null,
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
