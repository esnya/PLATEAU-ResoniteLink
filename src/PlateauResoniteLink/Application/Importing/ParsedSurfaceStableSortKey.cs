using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class ParsedSurfaceStableSortKey
{
    internal static string Create(ParsedSurface surface)
    {
        return Create(
            surface.PolygonId,
            (int)surface.Semantic,
            surface.ExteriorRing,
            surface.InteriorRings,
            static ring => ring.RingId,
            static ring => ring.Vertices,
            static ring => ring.UVs,
            WritePoint);
    }

    internal static string Create(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return Create(
            surface.PolygonId,
            (int)surface.Semantic,
            surface.ExteriorRing,
            surface.InteriorRings,
            static ring => ring.RingId,
            static ring => ring.Vertices,
            static ring => ring.UVs,
            WritePoint);
    }

    private static string Create<TRing, TPoint>(
        string polygonId,
        int semanticValue,
        TRing exteriorRing,
        IReadOnlyCollection<TRing> interiorRings,
        Func<TRing, string> getRingId,
        Func<TRing, IReadOnlyCollection<TPoint>> getVertices,
        Func<TRing, IReadOnlyList<Float2>?> getUvs,
        Action<BinaryWriter, TPoint> writePoint)
        where TPoint : notnull
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(polygonId);
            writer.Write(semanticValue);
            WriteRing(writer, exteriorRing, getRingId, getVertices, getUvs, writePoint);
            writer.Write(interiorRings.Count);
            foreach (TRing ring in interiorRings.OrderBy(getRingId, StringComparer.Ordinal))
            {
                WriteRing(writer, ring, getRingId, getVertices, getUvs, writePoint);
            }
        }

        byte[] hash = SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static void WriteRing<TRing, TPoint>(
        BinaryWriter writer,
        TRing ring,
        Func<TRing, string> getRingId,
        Func<TRing, IReadOnlyCollection<TPoint>> getVertices,
        Func<TRing, IReadOnlyList<Float2>?> getUvs,
        Action<BinaryWriter, TPoint> writePoint)
        where TPoint : notnull
    {
        writer.Write(getRingId(ring));
        IReadOnlyCollection<TPoint> vertices = getVertices(ring);
        writer.Write(vertices.Count);
        foreach (TPoint vertex in vertices)
        {
            writePoint(writer, vertex);
        }

        IReadOnlyList<Float2>? uvs = getUvs(ring);
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

    private static void WritePoint(BinaryWriter writer, GeodeticPoint vertex)
    {
        writer.Write(vertex.Latitude);
        writer.Write(vertex.Longitude);
        writer.Write(vertex.Altitude);
    }

    private static void WritePoint(BinaryWriter writer, LocalCityGmlObjectProjection.GeodeticPoint vertex)
    {
        writer.Write(vertex.Latitude);
        writer.Write(vertex.Longitude);
        writer.Write(vertex.Altitude);
    }
}
