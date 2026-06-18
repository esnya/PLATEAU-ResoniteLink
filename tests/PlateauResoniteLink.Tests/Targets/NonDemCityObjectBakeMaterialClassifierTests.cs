
using System.Collections.Generic;

using PlateauResoniteLink.Resonite.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemCityObjectBakeMaterialClassifierTests
{
    [Fact]
    public void ClassifySeparatesAtlasCandidateFromPreservedMaterialKinds()
    {
        Assert.Equal(
            NonDemMaterialBakeCategory.AtlasCandidate,
            NonDemCityObjectBakeMaterialClassifier.Classify(CreateDatasetTextureMaterial()));
        Assert.Equal(
            NonDemMaterialBakeCategory.PreservedVertexColor,
            NonDemCityObjectBakeMaterialClassifier.Classify(CreateTexturelessMaterial() with { MaterialType = ResoniteMaterialType.VertexColor }));
        Assert.Equal(
            NonDemMaterialBakeCategory.PreservedCommonMaterial,
            NonDemCityObjectBakeMaterialClassifier.Classify(CreateTexturelessMaterial() with
            {
                AssetBinding = ResoniteMaterialAssetBindingTestFactory.SharedGenericUv(),
            }));
        Assert.Equal(
            NonDemMaterialBakeCategory.PreservedTextureless,
            NonDemCityObjectBakeMaterialClassifier.Classify(CreateTexturelessMaterial()));
        Assert.Equal(
            NonDemMaterialBakeCategory.PreservedOther,
            NonDemCityObjectBakeMaterialClassifier.Classify(CreateDatasetTextureMaterial() with
            {
                Projection = ResoniteMaterialProjection.Triplanar,
            }));
    }

    [Fact]
    public void CanBufferCityObjectMaterialsHonorsRequiredAtlasCandidateAndPreservationFlags()
    {
        NonDemCityObjectBakePolicy requireAtlasCandidate = NonDemCityObjectBakePolicies.Default with
        {
            RequireAtlasCandidateMaterial = true,
            PreserveTexturelessMaterials = true,
        };

        Assert.False(NonDemCityObjectBakeMaterialClassifier.CanBufferCityObjectMaterials(
            CreateCityObject([CreateTexturelessMaterial()]),
            requireAtlasCandidate));
        Assert.True(NonDemCityObjectBakeMaterialClassifier.CanBufferCityObjectMaterials(
            CreateCityObject([CreateDatasetTextureMaterial()]),
            requireAtlasCandidate));
        Assert.False(NonDemCityObjectBakeMaterialClassifier.CanBufferCityObjectMaterials(
            CreateCityObject([CreateTexturelessMaterial()]),
            requireAtlasCandidate with
            {
                RequireAtlasCandidateMaterial = false,
                PreserveTexturelessMaterials = false,
            }));
    }

    [Fact]
    public void TryCreateMaterialBySubmeshIndexRejectsDuplicateSubmeshAssignments()
    {
        ResoniteConstructionCityObject cityObject = CreateCityObject(
            [
                CreateDatasetTextureMaterial(),
                CreateTexturelessMaterial(),
            ]);

        Assert.False(NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(
            cityObject,
            out _));
    }

    private static ResoniteConstructionCityObject CreateCityObject(IReadOnlyList<ResoniteMaterialBinding> materials)
    {
        return new ResoniteConstructionCityObject(
            "object",
            "object",
            "bldg",
            "53394525",
            LodLevel: 2,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(1.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 1.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 1.0)),
                ],
                [new ResoniteMeshSubmesh(0, [0, 1, 2])]),
            materials);
    }

    private static ResoniteMaterialBinding CreateDatasetTextureMaterial()
    {
        return CreateTexturelessMaterial() with
        {
            TexturePayload = new RawRgba32ResoniteTexturePayload(
                width: 1,
                height: 1,
                colorProfile: null,
                binaryPayload: [255, 255, 255, 255],
                description: "dataset.png"),
            TextureSourceKind = ResoniteTextureSourceKind.Dataset,
        };
    }

    private static ResoniteMaterialBinding CreateTexturelessMaterial()
    {
        return new ResoniteMaterialBinding(
            new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            ResoniteMaterialType.Standard,
            TexturePayload: null,
            ResoniteTextureSourceKind.Bundled,
            ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            ResoniteMaterialAssetBinding.Presentation);
    }
}
