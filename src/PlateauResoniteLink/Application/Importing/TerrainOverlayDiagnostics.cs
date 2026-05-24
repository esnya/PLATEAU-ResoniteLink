using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainOverlayDiagnostics
{
    internal static InvalidOperationException CreateMeshCodeMismatchException(
        string phase,
        string actualMeshCode,
        string? requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay? terrainOverlay)
    {
        string requestedMeshCodeBoundsSummary = requestedMeshCodeBounds is { Count: > 0 }
            ? string.Join(
                ",",
                requestedMeshCodeBounds.Select(static area => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatRounded(area.SouthLatitude)}-{FormatRounded(area.NorthLatitude)}-{FormatRounded(area.WestLongitude)}-{FormatRounded(area.EastLongitude)}")))
            : "<none>";
        string requestedMeshCodeSummary = requestedMeshCode is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"requested_mesh_code='{requestedMeshCode}', ");
        return new InvalidOperationException(
            $"Terrain overlay material requires a third-level mesh-code that matches the overlay geographic bounds. "
            + $"phase='{phase}', actual_mesh_code='{actualMeshCode}', {requestedMeshCodeSummary}"
            + $"requested_mesh_code_bounds='{requestedMeshCodeBoundsSummary}', overlay={FormatOverlay(terrainOverlay)}.");
    }

    internal static string FormatBounds(GeographicRectangle bounds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");

    private static string FormatOverlay(TerrainTextureOverlay? terrainOverlay)
    {
        return terrainOverlay is null
            ? "<null>"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"package='{terrainOverlay.PackageName}', bounds='{FormatBounds(terrainOverlay.GeographicBounds)}', sources='{terrainOverlay.SourceDescriptorKey}'");
    }

    internal static string FormatRounded(double value)
    {
        double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0.0 ? 0.0 : rounded).ToString("0.######", CultureInfo.InvariantCulture);
    }
}
