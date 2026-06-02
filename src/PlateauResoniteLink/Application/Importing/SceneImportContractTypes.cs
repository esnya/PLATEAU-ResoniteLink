using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportedSceneMetadata(
    string SchemaVersion,
    string SceneName,
    PlateauImportRequest Request,
    PlateauSourceDataset SourceDataset,
    Attribution Attribution,
    GeodeticOrigin GeodeticOrigin);

public sealed record Attribution(
    LicenseMetadata DatasetLicense);

public sealed record LicenseMetadata(
    bool RequireCredit,
    string CreditText,
    string LicenseName,
    string LicenseUrl);

public sealed record GeodeticOrigin(
    double Latitude,
    double Longitude,
    double Altitude);

public sealed record Float2(
    double X,
    double Y);

public sealed record Float3(
    double X,
    double Y,
    double Z);

public sealed record Quaternion(
    double X,
    double Y,
    double Z,
    double W);

public sealed record ColorRgba(
    double R,
    double G,
    double B,
    double A);

public sealed record Transform3D(
    Float3 Position,
    Quaternion? Rotation = null);

public abstract record ConstructionGeometry;

public sealed record TriangleMeshGeometry(
    ImportedMesh Mesh)
    : ConstructionGeometry;

public sealed record TerrainGridGeometry(
    int Width,
    int Height,
    Float2 Size,
    double MinHeight,
    double MaxHeight,
    IReadOnlyList<double> HeightSamples,
    IReadOnlyList<TerrainGridSampleCoverage> SampleCoverage,
    Float2? UvScale = null,
    Float2? UvOffset = null)
    : ConstructionGeometry;

public enum TerrainGridSampleCoverage
{
    Measured = 0,
    NoSurface = 1,
}

public sealed record DynamicTerrainGeometry(
    TriangleMeshGeometry StaticMesh,
    TerrainGridGeometry GridMesh)
    : ConstructionGeometry;

public sealed record ImportedMesh(
    IReadOnlyList<MeshVertex> Vertices,
    IReadOnlyList<MeshSubmesh> Submeshes);

public sealed record MeshVertex(
    Float3 Position,
    Float3 Normal,
    Float2 UV0,
    ColorRgba? Color = null);

public sealed record MeshSubmesh(
    int Index,
    IReadOnlyList<int> TriangleVertexIndices);

public enum TexturePayloadFormat
{
    RawRgba32 = 0,
    EncodedImage = 1,
}

public sealed record TexturePayload
{
    public TexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        RawTexturePayload.EnsureValidShape(width, height, binaryPayload.Length, RawTexturePayloadFormat.Rgba32);
        ImmutableArray<byte> immutablePayload = ImmutableArray.CreateRange(binaryPayload);
        string effectiveIdentity = identity ?? Guid.NewGuid().ToString("N");
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = immutablePayload;
        Identity = effectiveIdentity;
        Format = TexturePayloadFormat.RawRgba32;
        Source = TextureImportSourceFactory.CreateRawRgba32InMemory(
            width,
            height,
            colorProfile,
            immutablePayload,
            effectiveIdentity);
    }

    internal TexturePayload(
        int width,
        int height,
        string? colorProfile,
        IRawTexturePayloadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RawTexturePayload.EnsureValidDimensions(width, height);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = [];
        Identity = source.Identity;
        Format = TexturePayloadFormat.RawRgba32;
        Source = source;
    }

    public TexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        string effectiveIdentity = identity ?? source.Identity;
        if (!string.Equals(effectiveIdentity, source.Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Texture payload identity must match the texture import source identity.",
                nameof(identity));
        }

        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = [];
        Identity = effectiveIdentity;
        Format = TexturePayloadFormat.EncodedImage;
        Source = source;
    }

    public int? Width { get; }

    public int? Height { get; }

    public string? ColorProfile { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    public string Identity { get; }

    public TexturePayloadFormat Format { get; }

    public ITextureImportSource Source { get; }
}

public enum TextureSourceKind
{
    Dataset = 0,
    Bundled = 1,
}

public enum MaterialType
{
    Standard = 0,
    Wireframe = 1,
    VertexColor = 2,
}

public enum MaterialProjection
{
    Uv = 0,
    Triplanar = 1,
}

public sealed record MaterialDepthOffset(
    double Factor,
    double Units);

public sealed record MaterialOpticalProperties(
    ColorRgba? DiffuseColor = null,
    ColorRgba? EmissiveColor = null,
    ColorRgba? SpecularColor = null,
    double? AmbientIntensity = null,
    double? Shininess = null,
    double? Transparency = null);

public enum MaterialReuseScope
{
    PerObject = 0,
    Shared = 1,
}

public sealed record TerrainOverlayMaterialBinding(
    ThirdRegionalMeshCode MeshCode,
    TerrainTextureOverlay Overlay);

public sealed record MaterialBinding(
    ColorRgba BaseColor,
    MaterialType MaterialType,
    TexturePayload? TexturePayload,
    TextureSourceKind TextureSourceKind,
    MaterialProjection Projection,
    MaterialDepthOffset? DepthOffset,
    IReadOnlyList<int> SubmeshIndices,
    Float2? TextureScale = null,
    string? Family = null,
    Float2? TextureOffset = null,
    MaterialReuseScope ReuseScope = MaterialReuseScope.PerObject,
    TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
    int? BundledVariantIndex = null,
    DefaultCommonMaterialMember? CommonMaterial = null)
{
    public TerrainTextureOverlay? TerrainOverlay => TerrainOverlayMaterial?.Overlay;

    public string? TerrainMeshCode => TerrainOverlayMaterial?.MeshCode.Value;
}
