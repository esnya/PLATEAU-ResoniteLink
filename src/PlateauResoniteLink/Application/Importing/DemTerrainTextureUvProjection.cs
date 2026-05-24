using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal readonly record struct DemTerrainTextureUvProjection(
    double West,
    double South,
    double Width,
    double Height)
{
    public static DemTerrainTextureUvProjection? TryCreate(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        return TryCreate(cityObject.ActualMeshCode, demTerrainTextureOverlay);
    }

    public static DemTerrainTextureUvProjection? TryCreate(
        string actualMeshCode,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || TerrainTextureMeshCodeResolver.ResolveForOverlay(actualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
            || MeshCodeBounds.TryParse(terrainMeshCode) is not { } meshCodeBounds)
        {
            return null;
        }

        return Create(
            meshCodeBounds.WestLongitude,
            meshCodeBounds.EastLongitude,
            meshCodeBounds.NorthLatitude,
            meshCodeBounds.SouthLatitude);
    }

    public Float2 CreateUv(GeodeticPoint point)
    {
        double pointX = WebMercatorTileMath.LongitudeToNormalizedX(point.Longitude);
        double pointY = WebMercatorTileMath.LatitudeToNormalizedY(point.Latitude);
        double u = (pointX - West) / Width;
        double v = (South - pointY) / Height;

        return new Float2(u, v);
    }

    private static DemTerrainTextureUvProjection Create(
        double westLongitude,
        double eastLongitude,
        double northLatitude,
        double southLatitude)
    {
        double west = WebMercatorTileMath.LongitudeToNormalizedX(westLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(eastLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(northLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(southLatitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new DemTerrainTextureUvProjection(west, south, width, height);
    }
}
