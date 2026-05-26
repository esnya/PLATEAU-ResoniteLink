namespace PlateauResoniteLink.Application.Importing;

internal sealed record GeoTiffTagSnapshot(
    double[]? ModelTiePoint,
    double[]? PixelScale,
    double[]? ModelTransform,
    ushort[]? GeoKeyDirectory,
    double[]? GeoDoubleParams,
    string? GeoAsciiParams);
