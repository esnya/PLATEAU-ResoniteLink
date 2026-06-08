using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using PlateauResoniteLink.Application.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Formats;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CityGmlAppearanceStoreTests
{
    [Fact]
    public async Task Resolve_PreservesParameterizedTextureAndX3dMaterialAttributes()
    {
        using TemporaryDirectory datasetRoot = new();
        string packageDirectory = Path.Combine(datasetRoot.Path, "udx", "bldg", "53394525");
        string appearanceDirectory = Path.Combine(packageDirectory, "appearance");
        string texturePath = Path.Combine(appearanceDirectory, "roof.png");
        Directory.CreateDirectory(appearanceDirectory);

        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 255, 255, 255)))
        {
            await image.SaveAsPngAsync(texturePath);
        }

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            datasetRoot.Path,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        CityGmlAppearanceStore store = CityGmlAppearanceStore.Create(
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
        Assert.Equal(0.85, appearance.BaseColor.A, 6);
        Assert.NotNull(appearance.TexturePayload);
        Assert.NotNull(appearance.ParameterizedTexture);
        Assert.Equal("image/png", appearance.ParameterizedTexture!.MimeType);
        Assert.Equal(new FileInfo(texturePath).Length, appearance.TexturePayload!.Source.EstimatedByteLength);
        Assert.NotNull(appearance.MaterialAttributes);
        Assert.Equal(0.25, appearance.MaterialAttributes!.AmbientIntensity!.Value, 6);
        Assert.Equal(0.5, appearance.MaterialAttributes.Shininess!.Value, 6);
        Assert.Equal(0.15, appearance.MaterialAttributes.Transparency!.Value, 6);
        Assert.NotNull(appearance.MaterialAttributes.EmissiveColor);
        Assert.NotNull(appearance.MaterialAttributes.SpecularColor);
        Assert.NotNull(store.ResolveRingUvs("poly-1", "ring-1", vertexCount: 4));
    }

    [Fact]
    public async Task Resolve_InternsDatasetTexturePayloadsByResolvedFile()
    {
        using TemporaryDirectory datasetRoot = new();
        string packageDirectory = Path.Combine(datasetRoot.Path, "udx", "bldg", "53394525");
        string appearanceDirectory = Path.Combine(packageDirectory, "appearance");
        Directory.CreateDirectory(appearanceDirectory);

        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 255, 255, 255)))
        {
            await image.SaveAsPngAsync(Path.Combine(appearanceDirectory, "shared.png"));
            await image.SaveAsPngAsync(Path.Combine(appearanceDirectory, "other.png"));
        }

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            datasetRoot.Path,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        CityGmlAppearanceStore store = CityGmlAppearanceStore.Create(
            "udx/bldg/53394525/example.gml",
            datasetSource);
        XDocument document = XDocument.Parse(
            """
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/shared.png</app:imageURI>
                      <app:target uri="#poly-1" />
                      <app:target uri="#poly-2" />
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/other.png</app:imageURI>
                      <app:target uri="#poly-3" />
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
            </core:CityModel>
            """);

        store.LoadFromDocument(document);

        TexturePayload first = store.Resolve("poly-1").TexturePayload!;
        TexturePayload second = store.Resolve("poly-2").TexturePayload!;
        TexturePayload other = store.Resolve("poly-3").TexturePayload!;
        Assert.Same(first, second);
        Assert.NotSame(first, other);
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

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            datasetRoot.Path,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        CityGmlAppearanceStore store = CityGmlAppearanceStore.Create(
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

    [Fact]
    public async Task Resolve_IgnoresMalformedOptionalBorderColor()
    {
        using TemporaryDirectory datasetRoot = new();
        string packageDirectory = Path.Combine(datasetRoot.Path, "udx", "bldg", "53394525");
        string appearanceDirectory = Path.Combine(packageDirectory, "appearance");
        Directory.CreateDirectory(appearanceDirectory);

        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 255, 255, 255)))
        {
            await image.SaveAsPngAsync(Path.Combine(appearanceDirectory, "roof.png"));
        }

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            datasetRoot.Path,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        CityGmlAppearanceStore store = CityGmlAppearanceStore.Create(
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
                      <app:borderColor>broken value</app:borderColor>
                      <app:target uri="#poly-1">
                        <app:TexCoordList>
                          <app:textureCoordinates ring="#ring-1">0 0 1 0 1 1 0 1</app:textureCoordinates>
                        </app:TexCoordList>
                      </app:target>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
            </core:CityModel>
            """);

        store.LoadFromDocument(document);
        CityGmlResolvedAppearance appearance = store.Resolve("poly-1");

        Assert.NotNull(appearance.ParameterizedTexture);
        Assert.Null(appearance.ParameterizedTexture!.BorderColor);
        Assert.NotNull(appearance.TexturePayload);
    }

    [Fact]
    public async Task Resolve_IgnoresMalformedOptionalX3dMaterialFields()
    {
        using TemporaryDirectory datasetRoot = new();
        string packageDirectory = Path.Combine(datasetRoot.Path, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        IPlateauDatasetContentSource datasetSource = await PlateauDatasetContentSourceFactory.CreateAsync(
            datasetRoot.Path,
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        CityGmlAppearanceStore store = CityGmlAppearanceStore.Create(
            "udx/bldg/53394525/example.gml",
            datasetSource);
        XDocument document = XDocument.Parse(
            """
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:X3DMaterial>
                      <app:diffuseColor>0.2 0.4 0.6</app:diffuseColor>
                      <app:ambientIntensity>broken</app:ambientIntensity>
                      <app:emissiveColor>bad color</app:emissiveColor>
                      <app:specularColor>still bad</app:specularColor>
                      <app:shininess>oops</app:shininess>
                      <app:transparency>nope</app:transparency>
                      <app:target uri="#poly-1" />
                    </app:X3DMaterial>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
            </core:CityModel>
            """);

        store.LoadFromDocument(document);
        CityGmlResolvedAppearance appearance = store.Resolve("poly-1");

        Assert.NotNull(appearance.MaterialAttributes);
        Assert.Equal(0.2, appearance.MaterialAttributes!.DiffuseColor.R, 6);
        Assert.Null(appearance.MaterialAttributes.AmbientIntensity);
        Assert.Null(appearance.MaterialAttributes.EmissiveColor);
        Assert.Null(appearance.MaterialAttributes.SpecularColor);
        Assert.Null(appearance.MaterialAttributes.Shininess);
        Assert.Null(appearance.MaterialAttributes.Transparency);
    }

    [Fact]
    public async Task Resolve_DefersDatasetTextureReadUntilRawMaterialization()
    {
        byte[] pngBytes;
        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 0, 0, 255)))
        {
            using MemoryStream stream = new();
            await image.SaveAsPngAsync(stream);
            pngBytes = stream.ToArray();
        }

        CountingDatasetContentSource datasetSource = new(pngBytes);
        CityGmlAppearanceStore store = CityGmlAppearanceStore.Create(
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
                      <app:target uri="#poly-1" />
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
            </core:CityModel>
            """);

        store.LoadFromDocument(document);
        CityGmlResolvedAppearance appearance = store.Resolve("poly-1");

        Assert.NotNull(appearance.TexturePayload);
        Assert.Equal(0, datasetSource.OpenReadCallCount);

        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            appearance.TexturePayload!.Source,
            CancellationToken.None);

        Assert.Equal(1, datasetSource.OpenReadCallCount);
        Assert.Equal(1, rawPayload.Width);
        Assert.Equal(1, rawPayload.Height);
        Assert.Equal([255, 0, 0, 255], rawPayload.Bytes);
    }

    private sealed class CountingDatasetContentSource(byte[] payload) : IPlateauDatasetContentSource
    {
        public string SourcePath => "memory";

        public int OpenReadCallCount { get; private set; }

        public IReadOnlyList<string> EnumerateFiles() => ["udx/bldg/53394525/appearance/roof.png"];

        public bool FileExists(string relativePath) => true;

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return "udx/bldg/53394525/appearance/roof.png";
        }

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenReadCallCount++;
            return ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false));
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
