using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class MaterialGroupingPolicyTests
{
    [Fact]
    public void CreateKeyUsesTexturePayloadReferenceInsteadOfSourceDescription()
    {
        RawRgba32TexturePayload firstPayload = CreatePayload("same-description");
        RawRgba32TexturePayload secondPayload = CreatePayload("same-description");

        MaterialGroupingKey firstKey = MaterialGroupingPolicy.CreateKey(
            "53394525",
            CreateMaterial(firstPayload),
            depthOffset: null,
            textureScale: null,
            new ColorRgba(1.0, 1.0, 1.0, 1.0));
        MaterialGroupingKey repeatedFirstKey = MaterialGroupingPolicy.CreateKey(
            "53394525",
            CreateMaterial(firstPayload),
            depthOffset: null,
            textureScale: null,
            new ColorRgba(1.0, 1.0, 1.0, 1.0));
        MaterialGroupingKey secondKey = MaterialGroupingPolicy.CreateKey(
            "53394525",
            CreateMaterial(secondPayload),
            depthOffset: null,
            textureScale: null,
            new ColorRgba(1.0, 1.0, 1.0, 1.0));

        Assert.Equal(firstKey, repeatedFirstKey);
        Assert.NotEqual(firstKey, secondKey);
    }

    private static RawRgba32TexturePayload CreatePayload(string description)
    {
        return new RawRgba32TexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            binaryPayload: [255, 255, 255, 255],
            description);
    }

    private static ResolvedMaterial CreateMaterial(TexturePayload texturePayload)
    {
        return new ResolvedMaterial(
            MaterialType: MaterialType.Standard,
            TexturePayload: texturePayload,
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            ReuseScope: MaterialReuseScope.PerObject);
    }
}
