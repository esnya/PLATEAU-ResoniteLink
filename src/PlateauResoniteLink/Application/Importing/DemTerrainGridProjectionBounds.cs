using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record DemTerrainGridProjectionBounds(
    double MinX,
    double MaxX,
    double MinZ,
    double MaxZ,
    GeographicRectangle GeographicBounds)
{
    public double CenterX => (MinX + MaxX) / 2.0;

    public double CenterZ => (MinZ + MaxZ) / 2.0;

    public double ExtentX => MaxX - MinX;

    public double ExtentZ => MaxZ - MinZ;

    public bool HasUsableExtent => ExtentX > 1e-6 && ExtentZ > 1e-6;
}
