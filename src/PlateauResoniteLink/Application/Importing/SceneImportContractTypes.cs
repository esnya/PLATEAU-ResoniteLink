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

public sealed record TerrainGridGeometry : ConstructionGeometry
{
    public TerrainGridGeometry(
        int Width,
        int Height,
        Float2 Size,
        double MinHeight,
        double MaxHeight,
        IReadOnlyList<double> HeightSamples,
        IReadOnlyList<TerrainGridSampleCoverage> SampleCoverage,
        Float2? UvScale = null,
        Float2? UvOffset = null)
    {
        ArgumentNullException.ThrowIfNull(Size);
        ArgumentNullException.ThrowIfNull(HeightSamples);
        ArgumentNullException.ThrowIfNull(SampleCoverage);

        if (Width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), Width, "Terrain grid width must be at least 2.");
        }

        if (Height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), Height, "Terrain grid height must be at least 2.");
        }

        int sampleCount = checked(Width * Height);
        if (HeightSamples.Count != sampleCount)
        {
            throw new ArgumentException(
                $"Terrain grid height sample count {HeightSamples.Count} does not match sample count {sampleCount}.",
                nameof(HeightSamples));
        }

        if (SampleCoverage.Count != sampleCount)
        {
            throw new ArgumentException(
                $"Terrain grid sample coverage count {SampleCoverage.Count} does not match sample count {sampleCount}.",
                nameof(SampleCoverage));
        }

        this.Width = Width;
        this.Height = Height;
        this.Size = Size;
        this.MinHeight = MinHeight;
        this.MaxHeight = MaxHeight;
        this.HeightSamples = HeightSamples;
        this.SampleCoverage = SampleCoverage;
        this.UvScale = UvScale;
        this.UvOffset = UvOffset;
    }

    public int Width { get; init; }

    public int Height { get; init; }

    public Float2 Size { get; init; }

    public double MinHeight { get; init; }

    public double MaxHeight { get; init; }

    public IReadOnlyList<double> HeightSamples { get; init; }

    public IReadOnlyList<TerrainGridSampleCoverage> SampleCoverage { get; init; }

    public Float2? UvScale { get; init; }

    public Float2? UvOffset { get; init; }

    public int SampleCount => checked(Width * Height);

    public double HeightRange => Math.Max(MaxHeight - MinHeight, 0.0);

    public double GetWorldBaseHeight(Transform3D transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return transform.Position.Y - MaxHeight;
    }
}

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
    private protected TexturePayload(
        string? colorProfile,
        ITextureImportSource source)
    {
        ColorProfile = colorProfile;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string? ColorProfile { get; }

    public ITextureImportSource Source { get; }
}

public sealed record RawRgba32TexturePayload : TexturePayload
{
    public RawRgba32TexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? description = null)
        : this(
            width,
            height,
            colorProfile,
            binaryPayload,
            CreateSource(width, height, colorProfile, binaryPayload, description))
    {
    }

    private RawRgba32TexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        ITextureImportSource source)
        : base(colorProfile, source)
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
        string? description)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        return TextureImportSourceFactory.CreateInMemoryRaw(
            width,
            height,
            colorProfile,
            binaryPayload,
            description ?? "memory:raw-rgba32");
    }
}

public sealed record EncodedImageTexturePayload : TexturePayload
{
    public EncodedImageTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? description = null)
        : base(colorProfile, source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = description;
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

public record MaterialBinding(
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
            MaterialReuseScope.PerObject,
            TerrainOverlayMaterial,
            BundledVariantIndex)
    {
    }
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
            MaterialReuseScope.PerObject,
            TerrainOverlayMaterial,
            BundledVariantIndex,
            commonMaterial)
    {
    }
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
            MaterialReuseScope.Shared,
            TerrainOverlayMaterial,
            BundledVariantIndex,
            commonMaterial)
    {
    }
}
