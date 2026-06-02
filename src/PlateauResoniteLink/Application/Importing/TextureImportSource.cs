using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Application.Importing;

public abstract class RawTexturePayload
{
    private protected RawTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bytes);

        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        Bytes = bytes;
    }

    public int Width { get; }

    public int Height { get; }

    public string? ColorProfile { get; }

    public byte[] Bytes { get; }

    public abstract TResult Match<TResult>(
        Func<Rgba32RawTexturePayload, TResult> rgba32,
        Func<RgbaFloat32RawTexturePayload, TResult> rgbaFloat32);
}

public sealed class Rgba32RawTexturePayload : RawTexturePayload
{
    public Rgba32RawTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes)
        : base(width, height, colorProfile, bytes)
    {
    }

    public override TResult Match<TResult>(
        Func<Rgba32RawTexturePayload, TResult> rgba32,
        Func<RgbaFloat32RawTexturePayload, TResult> rgbaFloat32)
    {
        ArgumentNullException.ThrowIfNull(rgba32);
        ArgumentNullException.ThrowIfNull(rgbaFloat32);
        return rgba32(this);
    }
}

public sealed class RgbaFloat32RawTexturePayload : RawTexturePayload
{
    public RgbaFloat32RawTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes)
        : base(width, height, colorProfile, bytes)
    {
    }

    public override TResult Match<TResult>(
        Func<Rgba32RawTexturePayload, TResult> rgba32,
        Func<RgbaFloat32RawTexturePayload, TResult> rgbaFloat32)
    {
        ArgumentNullException.ThrowIfNull(rgba32);
        ArgumentNullException.ThrowIfNull(rgbaFloat32);
        return rgbaFloat32(this);
    }
}

public interface ITextureImportSource
{
    string Identity { get; }

    string Description { get; }

    string? ColorProfile { get; }

    long? EstimatedByteLength { get; }
}

internal interface IRgba32RawTexturePayloadSource : ITextureImportSource
{
    ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(CancellationToken cancellationToken);
}

internal interface IRgbaFloat32RawTexturePayloadSource : ITextureImportSource
{
    ValueTask<RgbaFloat32RawTexturePayload> MaterializeRgbaFloat32Async(CancellationToken cancellationToken);
}

internal static class TextureImportSourceMaterializer
{
    public static ValueTask<RawTexturePayload> MaterializeRawAsync(
        ITextureImportSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is IRgba32RawTexturePayloadSource rgba32Source)
        {
            return new ValueTask<RawTexturePayload>(MaterializeRgba32AsRawAsync(rgba32Source, cancellationToken));
        }

        if (source is IRgbaFloat32RawTexturePayloadSource rgbaFloat32Source)
        {
            return new ValueTask<RawTexturePayload>(MaterializeRgbaFloat32AsRawAsync(rgbaFloat32Source, cancellationToken));
        }

        throw new InvalidOperationException(
            $"Texture import source '{source.GetType().Name}' cannot materialize a raw texture payload.");

        static async Task<RawTexturePayload> MaterializeRgba32AsRawAsync(
            IRgba32RawTexturePayloadSource source,
            CancellationToken cancellationToken)
        {
            return await source.MaterializeRgba32Async(cancellationToken);
        }

        static async Task<RawTexturePayload> MaterializeRgbaFloat32AsRawAsync(
            IRgbaFloat32RawTexturePayloadSource source,
            CancellationToken cancellationToken)
        {
            return await source.MaterializeRgbaFloat32Async(cancellationToken);
        }
    }

    public static ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(
        ITextureImportSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not IRgba32RawTexturePayloadSource rgba32Source)
        {
            throw new InvalidOperationException(
                $"Texture import source '{source.GetType().Name}' cannot materialize an RGBA32 texture payload.");
        }

        return rgba32Source.MaterializeRgba32Async(cancellationToken);
    }
}

internal sealed class InMemoryRawTextureImportSource : IRgba32RawTexturePayloadSource
{
    private readonly byte[] bytes;

    public InMemoryRawTextureImportSource(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        this.bytes = (byte[])bytes.Clone();
        Identity = identity;
    }

    public int Width { get; }

    public int Height { get; }

    public string Identity { get; }

    public string Description => $"memory:{Identity}";

    public string? ColorProfile { get; }

    public long? EstimatedByteLength => bytes.Length;

    public ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new Rgba32RawTexturePayload(
            Width,
            Height,
            ColorProfile,
            (byte[])bytes.Clone()));
    }
}

internal sealed class InMemoryEncodedTextureImportSource : IRgba32RawTexturePayloadSource
{
    private readonly byte[] bytes;

    public InMemoryEncodedTextureImportSource(
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ColorProfile = colorProfile;
        this.bytes = (byte[])bytes.Clone();
        Identity = identity;
    }

    public string Identity { get; }

    public string Description => $"memory:{Identity}";

    public string? ColorProfile { get; }

    public long? EstimatedByteLength => bytes.Length;

    public async ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MemoryStream stream = new(bytes, writable: false);
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
    string identity) : IRgba32RawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description => $"dataset:{relativePath}";

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength => datasetSource is IPlateauDatasetContentLengthSource lengthSource
        ? lengthSource.TryGetFileLength(relativePath)
        : null;

    public async ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(image, ColorProfile);
    }
}

internal sealed class FileTextureImportSource(
    string absolutePath,
    string colorProfile,
    string identity) : IRgba32RawTexturePayloadSource
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

    public async ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(CancellationToken cancellationToken)
    {
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(absolutePath, cancellationToken);
        return TextureImportSourceFactory.CreateRawPayloadFromImage(image, ColorProfile);
    }
}

internal sealed class GeneratedRgba32TextureImportSource(
    Func<CancellationToken, ValueTask<Rgba32RawTexturePayload>> materializeRgba32Async,
    string identity,
    string description,
    string? colorProfile,
    long? estimatedByteLength = null) : IRgba32RawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description { get; } = description;

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength { get; } = estimatedByteLength;

    public ValueTask<Rgba32RawTexturePayload> MaterializeRgba32Async(CancellationToken cancellationToken)
    {
        return materializeRgba32Async(cancellationToken);
    }
}

internal sealed class GeneratedRgbaFloat32TextureImportSource(
    Func<CancellationToken, ValueTask<RgbaFloat32RawTexturePayload>> materializeRgbaFloat32Async,
    string identity,
    string description,
    string? colorProfile,
    long? estimatedByteLength = null) : IRgbaFloat32RawTexturePayloadSource
{
    public string Identity { get; } = identity;

    public string Description { get; } = description;

    public string? ColorProfile { get; } = colorProfile;

    public long? EstimatedByteLength { get; } = estimatedByteLength;

    public ValueTask<RgbaFloat32RawTexturePayload> MaterializeRgbaFloat32Async(CancellationToken cancellationToken)
    {
        return materializeRgbaFloat32Async(cancellationToken);
    }
}

internal static class TextureImportSourceFactory
{
    public static ITextureImportSource CreateInMemoryRaw(
        int width,
        int height,
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        return new InMemoryRawTextureImportSource(
            width,
            height,
            colorProfile,
            bytes,
            identity);
    }

    public static ITextureImportSource CreateInMemoryEncodedImage(
        string? colorProfile,
        byte[] bytes,
        string identity)
    {
        return new InMemoryEncodedTextureImportSource(
            colorProfile,
            bytes,
            identity);
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

    public static ITextureImportSource CreateGeneratedRgba32Image(
        Func<CancellationToken, ValueTask<Rgba32RawTexturePayload>> materializeRgba32Async,
        string identity,
        string description,
        string? colorProfile,
        long? estimatedByteLength = null)
    {
        return new GeneratedRgba32TextureImportSource(
            materializeRgba32Async,
            identity,
            description,
            colorProfile,
            estimatedByteLength);
    }

    public static ITextureImportSource CreateGeneratedRgbaFloat32Image(
        Func<CancellationToken, ValueTask<RgbaFloat32RawTexturePayload>> materializeRgbaFloat32Async,
        string identity,
        string description,
        string? colorProfile,
        long? estimatedByteLength = null)
    {
        return new GeneratedRgbaFloat32TextureImportSource(
            materializeRgbaFloat32Async,
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
        return CreateGeneratedRgba32Image(
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

    public static Rgba32RawTexturePayload CreateRawPayloadFromImage(
        Image<Rgba32> image,
        string? colorProfile)
    {
        ArgumentNullException.ThrowIfNull(image);
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new Rgba32RawTexturePayload(
            image.Width,
            image.Height,
            colorProfile,
            rawBytes);
    }
}
