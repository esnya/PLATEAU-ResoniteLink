using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Application.Importing;

public enum RawTexturePayloadFormat
{
    Rgba32 = 0,
    RgbaFloat32 = 1,
}

public sealed record RawTexturePayload
{
    private RawTexturePayload(
        int Width,
        int Height,
        string? ColorProfile,
        ImmutableArray<byte> Bytes,
        RawTexturePayloadFormat Format = RawTexturePayloadFormat.Rgba32)
    {
        if (Bytes.IsDefault)
        {
            throw new ArgumentException("Raw texture bytes must be initialized.", nameof(Bytes));
        }

        EnsureValidShape(Width, Height, Bytes.Length, Format);
        this.Width = Width;
        this.Height = Height;
        this.ColorProfile = ColorProfile;
        this.Bytes = Bytes;
        this.Format = Format;
    }

    internal static RawTexturePayload Create(
        int width,
        int height,
        string? colorProfile,
        ImmutableArray<byte> bytes,
        RawTexturePayloadFormat format = RawTexturePayloadFormat.Rgba32)
    {
        return new RawTexturePayload(width, height, colorProfile, bytes, format);
    }

    public RawTexturePayload(
        int Width,
        int Height,
        string? ColorProfile,
        byte[] Bytes,
        RawTexturePayloadFormat Format = RawTexturePayloadFormat.Rgba32)
    {
        ArgumentNullException.ThrowIfNull(Bytes);
        EnsureValidShape(Width, Height, Bytes.Length, Format);
        this.Width = Width;
        this.Height = Height;
        this.ColorProfile = ColorProfile;
        this.Bytes = ImmutableArray.CreateRange(Bytes);
        this.Format = Format;
    }

    public int Width { get; }

    public int Height { get; }

    public string? ColorProfile { get; }

    public ImmutableArray<byte> Bytes { get; }

    public RawTexturePayloadFormat Format { get; }

    internal static void EnsureValidShape(
        int width,
        int height,
        int byteLength,
        RawTexturePayloadFormat format)
    {
        EnsureValidDimensions(width, height);

        int bytesPerPixel = format switch
        {
            RawTexturePayloadFormat.Rgba32 => 4,
            RawTexturePayloadFormat.RgbaFloat32 => 4 * sizeof(float),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported raw texture payload format."),
        };
        long expectedByteLength = checked((long)width * height * bytesPerPixel);
        if (byteLength != expectedByteLength)
        {
            throw new ArgumentException(
                $"Raw texture byte length must be width * height * {bytesPerPixel}.",
                nameof(byteLength));
        }
    }

    internal static void EnsureValidDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Raw texture width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Raw texture height must be positive.");
        }
    }
}

public interface ITextureImportSource
{
    string Identity { get; }

    string Description { get; }

    string? ColorProfile { get; }

    long? EstimatedByteLength { get; }
}

internal interface IRawTexturePayloadSource : ITextureImportSource
{
    ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken);
}

internal static class TextureImportSourceMaterializer
{
    public static ValueTask<RawTexturePayload> MaterializeRawAsync(
        ITextureImportSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not IRawTexturePayloadSource rawSource)
        {
            throw new InvalidOperationException(
                $"Texture import source '{source.GetType().Name}' cannot materialize a raw texture payload.");
        }

        return rawSource.MaterializeRawAsync(cancellationToken);
    }
}

internal sealed class InMemoryRawTextureImportSource : IRawTexturePayloadSource
{
    private readonly ImmutableArray<byte> bytes;

    public InMemoryRawTextureImportSource(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        RawTexturePayload.EnsureValidShape(width, height, bytes.Length, RawTexturePayloadFormat.Rgba32);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        this.bytes = ImmutableArray.CreateRange(bytes);
        Identity = identity;
    }

    public InMemoryRawTextureImportSource(
        int width,
        int height,
        string? colorProfile,
        ImmutableArray<byte> bytes,
        string identity)
    {
        if (bytes.IsDefault)
        {
            throw new ArgumentException("Raw texture bytes must be initialized.", nameof(bytes));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        RawTexturePayload.EnsureValidShape(width, height, bytes.Length, RawTexturePayloadFormat.Rgba32);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        this.bytes = bytes;
        Identity = identity;
    }

    public int Width { get; }

    public int Height { get; }

    public string Identity { get; }

    public string Description => $"memory:{Identity}";

    public string? ColorProfile { get; }

    public long? EstimatedByteLength => bytes.Length;

    public ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RawTexturePayload.Create(
            Width,
            Height,
            ColorProfile,
            bytes));
    }
}

internal sealed class InMemoryEncodedTextureImportSource : IRawTexturePayloadSource
{
    private readonly ImmutableArray<byte> bytes;

    public InMemoryEncodedTextureImportSource(
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ColorProfile = colorProfile;
        this.bytes = ImmutableArray.CreateRange(bytes);
        Identity = identity;
    }

    public string Identity { get; }

    public string Description => $"memory:{Identity}";

    public string? ColorProfile { get; }

    public long? EstimatedByteLength => bytes.Length;

    public async ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        byte[] retainedBytes = ImmutableCollectionsMarshal.AsArray(bytes) ?? [];
        using MemoryStream stream = new(retainedBytes, writable: false);
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(
            image,
            ColorProfile);
    }
}

internal sealed class DatasetTextureImportSource(
    IPlateauDatasetContentSource datasetSource,
    string relativePath,
    string? colorProfile,
    string identity) : IRawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description => $"dataset:{relativePath}";

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength => datasetSource is IPlateauDatasetContentLengthSource lengthSource
        ? lengthSource.TryGetFileLength(relativePath)
        : null;

    public async ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(image, ColorProfile);
    }
}

internal sealed class FileTextureImportSource(
    string absolutePath,
    string colorProfile,
    string identity) : IRawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description => $"file:{Path.GetFileName(absolutePath)}";

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength
    {
        get
        {
            try
            {
                return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public async ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(absolutePath, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(image, ColorProfile);
    }
}

internal sealed class GeneratedTextureImportSource(
    Func<CancellationToken, ValueTask<RawTexturePayload>> materializeRawAsync,
    string identity,
    string description,
    string? colorProfile,
    long? estimatedByteLength = null) : IRawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description { get; } = description;

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength { get; } = estimatedByteLength;

    public ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        return materializeRawAsync(cancellationToken);
    }
}

internal static class TextureImportSourceFactory
{
    public static ITextureImportSource CreateRawRgba32InMemory(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        return new InMemoryRawTextureImportSource(width, height, colorProfile, bytes, identity);
    }

    internal static ITextureImportSource CreateRawRgba32InMemory(
        int width,
        int height,
        string? colorProfile,
        ImmutableArray<byte> bytes,
        string identity)
    {
        return new InMemoryRawTextureImportSource(width, height, colorProfile, bytes, identity);
    }

    public static ITextureImportSource CreateEncodedImageInMemory(
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        return new InMemoryEncodedTextureImportSource(colorProfile, bytes, identity);
    }

    public static ITextureImportSource CreateDatasetEncodedImage(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        string? colorProfile,
        string identity)
    {
        return new DatasetTextureImportSource(datasetSource, relativePath, colorProfile, identity);
    }

    public static ITextureImportSource CreateFileImage(
        string absolutePath,
        string colorProfile,
        string? identity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);
        return new FileTextureImportSource(
            absolutePath,
            colorProfile,
            identity ?? $"file:{Path.GetFullPath(absolutePath)}:{colorProfile}");
    }

    public static ITextureImportSource CreateGeneratedImage(
        Func<CancellationToken, ValueTask<RawTexturePayload>> materializeRawAsync,
        string identity,
        string description,
        string? colorProfile,
        long? estimatedByteLength = null)
    {
        return new GeneratedTextureImportSource(
            materializeRawAsync,
            identity,
            description,
            colorProfile,
            estimatedByteLength);
    }

    public static ITextureImportSource CreateGeneratedImageFromClone(
        Image<Rgba32> image,
        string identity,
        string description,
        string? colorProfile)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image<Rgba32> retainedImage = image.Clone();
        object gate = new();
        return CreateGeneratedImage(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    return ValueTask.FromResult(CreateRawPayloadFromImage(retainedImage, colorProfile));
                }
            },
            identity,
            description,
            colorProfile,
            (long)image.Width * image.Height * 4);
    }

    public static RawTexturePayload CreateRawPayloadFromImage(
        Image<Rgba32> image,
        string? colorProfile)
    {
        ArgumentNullException.ThrowIfNull(image);
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new RawTexturePayload(
            image.Width,
            image.Height,
            colorProfile,
            rawBytes);
    }
}
