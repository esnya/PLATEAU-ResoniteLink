using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class ParsedSurfaceStableSortKey
{
    public static string Create(ParsedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(surface.PolygonId);
            writer.Write((int)surface.Semantic);
            WriteRing(writer, surface.ExteriorRing);
            writer.Write(surface.InteriorRings.Length);
            foreach (ParsedRing ring in surface.InteriorRings.OrderBy(static ring => ring.RingId, StringComparer.Ordinal))
            {
                WriteRing(writer, ring);
            }
        }

        byte[] hash = SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static void WriteRing(BinaryWriter writer, ParsedRing ring)
    {
        writer.Write(ring.RingId);
        writer.Write(ring.Vertices.Length);
        foreach (GeodeticPoint vertex in ring.Vertices)
        {
            writer.Write(vertex.Latitude);
            writer.Write(vertex.Longitude);
            writer.Write(vertex.Altitude);
        }

        WriteUvs(writer, ring.UVs);
    }

    private static void WriteUvs(BinaryWriter writer, IReadOnlyList<Float2>? uvs)
    {
        writer.Write(uvs?.Count ?? -1);
        if (uvs is null)
        {
            return;
        }

        foreach (Float2 uv in uvs)
        {
            writer.Write(uv.X);
            writer.Write(uv.Y);
        }
    }
}
