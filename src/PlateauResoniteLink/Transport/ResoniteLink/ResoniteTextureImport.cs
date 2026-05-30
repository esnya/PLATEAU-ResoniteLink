using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal abstract record ResoniteTextureImport : IRawTexturePayloadSource
{
    public abstract string Identity { get; }

    public abstract string Description { get; }

    public abstract string ColorProfile { get; init; }

    public abstract long? EstimatedByteLength { get; }

    public abstract ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken);
}

internal static class ResoniteTextureColorProfiles
{
    public const string Linear = "Linear";
    public const string Srgb = "sRGB";
}

internal sealed record ResoniteRawTextureImport(
    int Width,
    int Height,
    string ColorProfile,
    byte[] RawRgba32Bytes) : ResoniteTextureImport
{
    public override string Identity => $"raw-rgba32:{Width}:{Height}:{RuntimeHelpers.GetHashCode(RawRgba32Bytes)}";

    public override string Description => "raw-rgba32-memory";

    public override long? EstimatedByteLength => RawRgba32Bytes.Length;

    public override ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RawTexturePayload(
            Width,
            Height,
            ColorProfile,
            (byte[])RawRgba32Bytes.Clone()));
    }
}

internal sealed record ResoniteRawHdrTextureImport(
    int Width,
    int Height,
    byte[] RawRgbaFloatBytes) : ResoniteTextureImport
{
    public override string Identity => $"raw-rgba-float32:{Width}:{Height}:{RuntimeHelpers.GetHashCode(RawRgbaFloatBytes)}";

    public override string Description => "raw-rgba-float32-memory";

    public override string ColorProfile { get; init; } = string.Empty;

    public override long? EstimatedByteLength => RawRgbaFloatBytes.Length;

    public override ValueTask<RawTexturePayload> MaterializeRawAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RawTexturePayload(
            Width,
            Height,
            ColorProfile,
            (byte[])RawRgbaFloatBytes.Clone(),
            RawTexturePayloadFormat.RgbaFloat32));
    }
}
