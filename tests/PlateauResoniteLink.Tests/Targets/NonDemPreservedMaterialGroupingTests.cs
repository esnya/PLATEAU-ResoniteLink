
using PlateauResoniteLink.Resonite.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemPreservedMaterialGroupingTests
{
    [Fact]
    public void CreateKeyIgnoresCommonMaterialTintButKeepsTextureReference()
    {
        ResoniteMaterialBinding commonMaterial = CreateTexturelessMaterial() with
        {
            AssetBinding = ResoniteMaterialAssetBindingTestFactory.SharedGenericUv(),
        };
        ResoniteTexturePayload texture = new RawRgba32ResoniteTexturePayload(
            width: 1,
            height: 1,
            colorProfile: null,
            binaryPayload: [255, 255, 255, 255],
            description: "shared.png");

        NonDemPreservedMaterialGroupingKey first = NonDemPreservedMaterialGrouping.CreateKey(commonMaterial with
        {
            BaseColor = new ResoniteColor(1.0, 0.0, 0.0, 1.0),
            TexturePayload = texture,
        });
        NonDemPreservedMaterialGroupingKey second = NonDemPreservedMaterialGrouping.CreateKey(commonMaterial with
        {
            BaseColor = new ResoniteColor(0.0, 0.0, 1.0, 1.0),
            TexturePayload = texture,
        });
        NonDemPreservedMaterialGroupingKey differentTexture = NonDemPreservedMaterialGrouping.CreateKey(commonMaterial with
        {
            TexturePayload = new RawRgba32ResoniteTexturePayload(
                width: 1,
                height: 1,
                colorProfile: null,
                binaryPayload: [255, 255, 255, 255],
                description: "shared.png"),
        });

        Assert.True(NonDemPreservedMaterialGrouping.KeyComparer.Equals(first, second));
        Assert.False(NonDemPreservedMaterialGrouping.KeyComparer.Equals(first, differentTexture));
    }

    [Fact]
    public void CreateKeyKeepsDedicatedMaterialTintDistinct()
    {
        NonDemPreservedMaterialGroupingKey red = NonDemPreservedMaterialGrouping.CreateKey(CreateTexturelessMaterial() with
        {
            BaseColor = new ResoniteColor(1.0, 0.0, 0.0, 1.0),
        });
        NonDemPreservedMaterialGroupingKey blue = NonDemPreservedMaterialGrouping.CreateKey(CreateTexturelessMaterial() with
        {
            BaseColor = new ResoniteColor(0.0, 0.0, 1.0, 1.0),
        });

        Assert.False(NonDemPreservedMaterialGrouping.KeyComparer.Equals(red, blue));
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
