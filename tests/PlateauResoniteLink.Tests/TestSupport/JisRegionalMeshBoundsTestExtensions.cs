using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.TestSupport;

internal static class JisRegionalMeshBoundsTestExtensions
{
    public static GeographicRectangle ToGeographicRectangle(this JisRegionalMeshBounds bounds)
    {
        return new GeographicRectangle(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }
}
