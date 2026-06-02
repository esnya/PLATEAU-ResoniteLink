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

public abstract record TexturePayload
{
    private protected TexturePayload(ITextureImportSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Identity);

        Source = source;
    }

    public ITextureImportSource Source { get; }
}

public sealed record RawRgba32TexturePayload : TexturePayload
{
    public RawRgba32TexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity = null)
        : this(
            width,
            height,
            colorProfile,
            binaryPayload,
            CreateSource(width, height, colorProfile, binaryPayload, identity))
    {
    }

    private RawRgba32TexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        ITextureImportSource source)
        : base(source)
    {
        Rgba32RawTexturePayload.ValidateByteLength(width, height, binaryPayload);
        Width = width;
        Height = height;
        BinaryPayload = ImmutableArray.CreateRange(binaryPayload);
    }

    public int Width { get; }

    public int Height { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    private static ITextureImportSource CreateSource(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        string effectiveIdentity = identity ?? Guid.NewGuid().ToString("N");
        return TextureImportSourceFactory.CreateInMemoryRaw(
            width,
            height,
            colorProfile,
            binaryPayload,
            effectiveIdentity);
    }
}

public sealed record EncodedImageTexturePayload : TexturePayload
{
    public EncodedImageTexturePayload(
        int? width,
        int? height,
        ITextureImportSource source)
        : base(source)
    {
        Width = width;
        Height = height;
    }

    public int? Width { get; }

    public int? Height { get; }
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

public abstract record MaterialBinding(
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
    TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
    int? BundledVariantIndex = null)
{
    public TerrainTextureOverlay? TerrainOverlay => TerrainOverlayMaterial?.Overlay;

    public string? TerrainMeshCode => TerrainOverlayMaterial?.MeshCode.Value;

    public abstract MaterialReuseScope ReuseScope { get; }

    public virtual DefaultCommonMaterialMember? CommonMaterial => null;
}

public sealed record PresentationMaterialBinding : MaterialBinding
{
    public PresentationMaterialBinding(
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
        TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
        int? BundledVariantIndex = null)
        : base(
            BaseColor,
            MaterialType,
            TexturePayload,
            TextureSourceKind,
            Projection,
            DepthOffset,
            SubmeshIndices,
            TextureScale,
            Family,
            TextureOffset,
            TerrainOverlayMaterial,
            BundledVariantIndex)
    {
    }

    public override MaterialReuseScope ReuseScope => MaterialReuseScope.PerObject;
}

public sealed record PresentationCommonMaterialBinding : MaterialBinding
{
    public PresentationCommonMaterialBinding(
        ColorRgba BaseColor,
        MaterialType MaterialType,
        TexturePayload? TexturePayload,
        TextureSourceKind TextureSourceKind,
        MaterialProjection Projection,
        MaterialDepthOffset? DepthOffset,
        IReadOnlyList<int> SubmeshIndices,
        DefaultCommonMaterialMember commonMaterial,
        Float2? TextureScale = null,
        string? Family = null,
        Float2? TextureOffset = null,
        TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
        int? BundledVariantIndex = null)
        : base(
            BaseColor,
            MaterialType,
            TexturePayload,
            TextureSourceKind,
            Projection,
            DepthOffset,
            SubmeshIndices,
            TextureScale,
            Family,
            TextureOffset,
            TerrainOverlayMaterial,
            BundledVariantIndex)
    {
        CommonMaterial = commonMaterial ?? throw new ArgumentNullException(nameof(commonMaterial));
    }

    public override MaterialReuseScope ReuseScope => MaterialReuseScope.PerObject;

    public override DefaultCommonMaterialMember CommonMaterial { get; }
}

public sealed record SharedCommonMaterialBinding : MaterialBinding
{
    public SharedCommonMaterialBinding(
        ColorRgba BaseColor,
        MaterialType MaterialType,
        TexturePayload? TexturePayload,
        TextureSourceKind TextureSourceKind,
        MaterialProjection Projection,
        MaterialDepthOffset? DepthOffset,
        IReadOnlyList<int> SubmeshIndices,
        DefaultCommonMaterialMember commonMaterial,
        Float2? TextureScale = null,
        string? Family = null,
        Float2? TextureOffset = null,
        TerrainOverlayMaterialBinding? TerrainOverlayMaterial = null,
        int? BundledVariantIndex = null)
        : base(
            BaseColor,
            MaterialType,
            TexturePayload,
            TextureSourceKind,
            Projection,
            DepthOffset,
            SubmeshIndices,
            TextureScale,
            Family,
            TextureOffset,
            TerrainOverlayMaterial,
            BundledVariantIndex)
    {
        CommonMaterial = commonMaterial ?? throw new ArgumentNullException(nameof(commonMaterial));
    }

    public override MaterialReuseScope ReuseScope => MaterialReuseScope.Shared;

    public override DefaultCommonMaterialMember CommonMaterial { get; }
}
