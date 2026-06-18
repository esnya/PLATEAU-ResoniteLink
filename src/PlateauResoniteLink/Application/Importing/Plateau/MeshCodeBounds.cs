using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing.Plateau;

internal sealed record MeshCodeBounds(
    double SouthLatitude,
    double NorthLatitude,
    double WestLongitude,
    double EastLongitude)
{
    public static MeshCodeBounds? TryParse(string meshCode)
    {
        if (!PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds))
        {
            return null;
        }

        return new MeshCodeBounds(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }

    public static MeshCodeBounds Parse(string meshCode)
    {
        return TryParse(meshCode)
            ?? throw new ArgumentException("Selected mesh code must be a valid PLATEAU mesh code with resolvable geographic bounds.", nameof(meshCode));
    }

    public static MeshCodeBounds[] CreateManyFromSelectedMeshCodes(IEnumerable<string> meshCodes)
    {
        ArgumentNullException.ThrowIfNull(meshCodes);

        return meshCodes
            .Select(Parse)
            .Distinct()
            .ToArray();
    }

    public static MeshCodeBounds? TryMerge(IEnumerable<MeshCodeBounds> meshAreas)
    {
        ArgumentNullException.ThrowIfNull(meshAreas);

        MeshCodeBounds[] areaArray = meshAreas.ToArray();
        if (areaArray.Length == 0)
        {
            return null;
        }

        return new MeshCodeBounds(
            areaArray.Min(static meshArea => meshArea.SouthLatitude),
            areaArray.Max(static meshArea => meshArea.NorthLatitude),
            areaArray.Min(static meshArea => meshArea.WestLongitude),
            areaArray.Max(static meshArea => meshArea.EastLongitude));
    }

    public GeodeticCoordinate GetGeodeticCenter()
    {
        return new GeodeticCoordinate(
            Latitude: (SouthLatitude + NorthLatitude) / 2.0,
            Longitude: (WestLongitude + EastLongitude) / 2.0,
            Altitude: 0.0);
    }
}
