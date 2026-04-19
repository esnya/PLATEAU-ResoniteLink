using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.Formats;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CityGmlAppearanceStoreTests
{
    [Fact]
    public async Task Resolve_PreservesParameterizedTextureAndX3dMaterialAttributes()
    {
        using TemporaryDirectory datasetRoot = new();
        string packageDirectory = Path.Combine(datasetRoot.Path, "udx", "bldg", "53394525");
        string appearanceDirectory = Path.Combine(packageDirectory, "appearance");
        Directory.CreateDirectory(appearanceDirectory);

        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 255, 255, 255)))
        {
            await image.SaveAsPngAsync(Path.Combine(appearanceDirectory, "roof.png"));
        }

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(datasetRoot.Path);
        ICityGmlAppearanceStore store = new CityGmlAppearanceStoreFactory().Create(
            "udx/bldg/53394525/example.gml",
            datasetSource);
        XDocument document = XDocument.Parse(
            """
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/roof.png</app:imageURI>
                      <app:mimeType>image/png</app:mimeType>
                      <app:target uri="#poly-1">
                        <app:TexCoordList>
                          <app:textureCoordinates ring="#ring-1">0 0 1 0 1 1 0 1</app:textureCoordinates>
                        </app:TexCoordList>
                      </app:target>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                  <app:surfaceDataMember>
                    <app:X3DMaterial>
                      <app:ambientIntensity>0.25</app:ambientIntensity>
                      <app:diffuseColor>0.2 0.4 0.6</app:diffuseColor>
                      <app:emissiveColor>0.1 0.2 0.3</app:emissiveColor>
                      <app:specularColor>0.7 0.8 0.9</app:specularColor>
                      <app:shininess>0.5</app:shininess>
                      <app:transparency>0.15</app:transparency>
                      <app:target uri="#poly-1" />
                    </app:X3DMaterial>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
            </core:CityModel>
            """);

        store.LoadFromDocument(document);
        CityGmlResolvedAppearance appearance = store.Resolve("poly-1");

        Assert.Equal(0.2, appearance.BaseColor.R, 6);
        Assert.NotNull(appearance.TexturePayload);
        Assert.NotNull(appearance.ParameterizedTexture);
        Assert.Equal("image/png", appearance.ParameterizedTexture!.MimeType);
        Assert.NotNull(appearance.MaterialAttributes);
        Assert.Equal(0.25, appearance.MaterialAttributes!.AmbientIntensity!.Value, 6);
        Assert.Equal(0.5, appearance.MaterialAttributes.Shininess!.Value, 6);
        Assert.Equal(0.15, appearance.MaterialAttributes.Transparency!.Value, 6);
        Assert.NotNull(appearance.MaterialAttributes.EmissiveColor);
        Assert.NotNull(appearance.MaterialAttributes.SpecularColor);
        Assert.True(appearance.RingUvsByRingId!.ContainsKey("ring-1"));
    }

    [Fact]
    public async Task Resolve_ExposesGeoreferencedTextureMetadataWithoutDatasetPayload()
    {
        using TemporaryDirectory datasetRoot = new();
        string packageDirectory = Path.Combine(datasetRoot.Path, "udx", "bldg", "53394525");
        string appearanceDirectory = Path.Combine(packageDirectory, "appearance");
        Directory.CreateDirectory(appearanceDirectory);

        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 255, 255, 255)))
        {
            await image.SaveAsPngAsync(Path.Combine(appearanceDirectory, "ortho.png"));
        }

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(datasetRoot.Path);
        ICityGmlAppearanceStore store = new CityGmlAppearanceStoreFactory().Create(
            "udx/bldg/53394525/example.gml",
            datasetSource);
        XDocument document = XDocument.Parse(
            """
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:GeoreferencedTexture>
                      <app:imageURI>appearance/ortho.png</app:imageURI>
                      <app:mimeType>image/png</app:mimeType>
                      <app:referencePoint>35.0 139.0 10.0</app:referencePoint>
                      <app:orientation>0 1</app:orientation>
                      <app:target uri="#poly-geo" />
                    </app:GeoreferencedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
            </core:CityModel>
            """);

        store.LoadFromDocument(document);
        CityGmlResolvedAppearance appearance = store.Resolve("poly-geo");

        Assert.Equal(1.0, appearance.BaseColor.R, 6);
        Assert.Null(appearance.TexturePayload);
        Assert.NotNull(appearance.GeoreferencedTexture);
        Assert.Equal("image/png", appearance.GeoreferencedTexture!.MimeType);
        Assert.Equal("35.0 139.0 10.0", appearance.GeoreferencedTexture.ReferencePoint);
        Assert.Equal("0 1", appearance.GeoreferencedTexture.Orientation);
    }
}
