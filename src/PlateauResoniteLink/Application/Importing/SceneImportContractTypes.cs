using System;
using System.Collections.Generic;
using System.IO;

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
    LicenseMetadata DatasetLicense,
    IReadOnlyList<MaterialAttribution> MaterialLicenses);

public sealed record MaterialAttribution(
    string MaterialKey,
    LicenseMetadata? License);

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

public sealed record HeightMapGridGeometry(
    int Width,
    int Height,
    Float2 Size,
    double MinHeight,
    double MaxHeight,
    IReadOnlyList<double> HeightSamples,
    Float2? UvScale = null,
    Float2? UvOffset = null)
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
    string MaterialKey,
    IReadOnlyList<int> TriangleVertexIndices);

public enum TexturePayloadFormat
{
    RawRgba32 = 0,
    EncodedImage = 1,
}

public sealed record TexturePayload
{
    private readonly byte[] binaryPayloadBytes;

    public TexturePayload(
        int? Width,
        int? Height,
        string? ColorProfile,
        Stream BinaryPayload,
        string? Identity = null,
        TexturePayloadFormat Format = TexturePayloadFormat.RawRgba32)
    {
        this.Width = Width;
        this.Height = Height;
        this.ColorProfile = ColorProfile;
        binaryPayloadBytes = CopyBinaryPayload(BinaryPayload);
        this.Identity = Identity;
        this.Format = Format;
    }

    public TexturePayload(
        int? Width,
        int? Height,
        string? ColorProfile,
        byte[] BinaryPayload,
        string? Identity = null,
        TexturePayloadFormat Format = TexturePayloadFormat.RawRgba32)
        : this(Width, Height, ColorProfile, CreateBinaryPayloadStream(BinaryPayload), Identity, Format)
    {
    }

    public int? Width { get; }

    public int? Height { get; }

    public string? ColorProfile { get; }

    public Stream BinaryPayload => CreateBinaryPayloadStream(binaryPayloadBytes);

    public string? Identity { get; }

    public TexturePayloadFormat Format { get; }

    internal byte[] CopyBinaryPayloadToArray() => (byte[])binaryPayloadBytes.Clone();

    private static MemoryStream CreateBinaryPayloadStream(byte[] binaryPayload)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        return new MemoryStream(binaryPayload, writable: false);
    }

    private static byte[] CopyBinaryPayload(Stream binaryPayload)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        if (!binaryPayload.CanRead)
        {
            throw new ArgumentException("Texture payload stream must be readable.", nameof(binaryPayload));
        }

        long originalPosition = 0;
        bool restorePosition = binaryPayload.CanSeek;
        if (restorePosition)
        {
            originalPosition = binaryPayload.Position;
        }

        try
        {
            using MemoryStream copy = new();
            binaryPayload.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            if (restorePosition)
            {
                binaryPayload.Position = originalPosition;
            }
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

public enum MaterialReuseScope
{
    PerObject = 0,
    Shared = 1,
}

public sealed record MaterialBinding(
    string MaterialKey,
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
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null);
