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
        int? width,
        int? height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity = null,
        TexturePayloadFormat format = TexturePayloadFormat.RawRgba32)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(binaryPayload);
        BinaryPayload = ImmutableArray.CreateRange(binaryPayload);
        Identity = identity;
        ValidateFormat(format);
        Format = format;
        Source = TextureImportSourceFactory.CreateInMemory(
            width,
            height,
            colorProfile,
            binaryPayload,
            identity ?? Guid.NewGuid().ToString("N"),
            format);
    }

    public TexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? identity = null,
        TexturePayloadFormat format = TexturePayloadFormat.EncodedImage)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(source);
        BinaryPayload = [];
        Identity = identity ?? source.Identity;
        ValidateFormat(format);
        Format = format;
        Source = source;
    }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? ColorProfile { get; init; }

    public ImmutableArray<byte> BinaryPayload { get; init; }

    public string? Identity { get; init; }

    public TexturePayloadFormat Format { get; init; }

    public ITextureImportSource Source { get; init; }

    private static void ValidateFormat(TexturePayloadFormat format)
    {
        if (format is not (TexturePayloadFormat.RawRgba32 or TexturePayloadFormat.EncodedImage))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported texture payload format.");
        }
    }
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
