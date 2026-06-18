namespace PlateauResoniteLink.Application.Importing.Plateau;

internal sealed record GeoTiffTagSnapshot(
    double[]? ModelTiePoint,
    double[]? PixelScale,
    double[]? ModelTransform,
    ushort[]? GeoKeyDirectory,
    double[]? GeoDoubleParams,
    string? GeoAsciiParams);
