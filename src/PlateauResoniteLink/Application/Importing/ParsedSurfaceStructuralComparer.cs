using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class ParsedSurfaceStructuralComparer : IComparer<ParsedSurface>
{
    public static readonly ParsedSurfaceStructuralComparer Instance = new();

    private ParsedSurfaceStructuralComparer()
    {
    }

    public int Compare(ParsedSurface? left, ParsedSurface? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        int result = left.Semantic.CompareTo(right.Semantic);
        if (result != 0)
        {
            return result;
        }

        result = CompareRing(left.ExteriorRing, right.ExteriorRing);
        if (result != 0)
        {
            return result;
        }

        result = left.InteriorRings.Length.CompareTo(right.InteriorRings.Length);
        if (result != 0)
        {
            return result;
        }

        for (int index = 0; index < left.InteriorRings.Length; index++)
        {
            result = CompareRing(left.InteriorRings[index], right.InteriorRings[index]);
            if (result != 0)
            {
                return result;
            }
        }

        result = CompareColor(left.BaseColor, right.BaseColor);
        if (result != 0)
        {
            return result;
        }

        result = left.UsesGeneratedDemTexture.CompareTo(right.UsesGeneratedDemTexture);
        if (result != 0)
        {
            return result;
        }

        return (left.TexturePayload is not null).CompareTo(right.TexturePayload is not null);
    }

    private static int CompareRing(ParsedRing left, ParsedRing right)
    {
        int result = left.Vertices.Length.CompareTo(right.Vertices.Length);
        if (result != 0)
        {
            return result;
        }

        for (int index = 0; index < left.Vertices.Length; index++)
        {
            result = ComparePoint(left.Vertices[index], right.Vertices[index]);
            if (result != 0)
            {
                return result;
            }
        }

        IReadOnlyList<Float2>? leftUvs = left.UVs;
        IReadOnlyList<Float2>? rightUvs = right.UVs;
        result = (leftUvs?.Count ?? -1).CompareTo(rightUvs?.Count ?? -1);
        if (result != 0 || leftUvs is null || rightUvs is null)
        {
            return result;
        }

        for (int index = 0; index < leftUvs.Count; index++)
        {
            result = CompareFloat2(leftUvs[index], rightUvs[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private static int ComparePoint(GeodeticPoint left, GeodeticPoint right)
    {
        int result = left.Latitude.CompareTo(right.Latitude);
        if (result != 0)
        {
            return result;
        }

        result = left.Longitude.CompareTo(right.Longitude);
        return result != 0 ? result : left.Altitude.CompareTo(right.Altitude);
    }

    private static int CompareFloat2(Float2 left, Float2 right)
    {
        int result = left.X.CompareTo(right.X);
        return result != 0 ? result : left.Y.CompareTo(right.Y);
    }

    private static int CompareColor(ColorRgba left, ColorRgba right)
    {
        int result = left.R.CompareTo(right.R);
        if (result != 0)
        {
            return result;
        }

        result = left.G.CompareTo(right.G);
        if (result != 0)
        {
            return result;
        }

        result = left.B.CompareTo(right.B);
        return result != 0 ? result : left.A.CompareTo(right.A);
    }
}
