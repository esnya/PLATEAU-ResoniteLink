using System;

namespace PlateauResoniteLink.Domain.Importing;

public static class DemTerrainTextureDefaults
{
    public const string PlateauOrthoPath = "dem/plateau-ortho";
    public const string PlateauOrthoUrlTemplate = "https://api.plateauview.mlit.go.jp/tiles/plateau-ortho-2023/{z}/{x}/{y}.png";
    public const string GsiFallbackUrlTemplate = "https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg";
    public const int PlateauOrthoZoomLevel = 19;
    public const int FallbackZoomLevel = 18;
    public const int MaxTextureSize = 8192;

    public static TerrainTextureOverlay CreatePlateauOrthoWithGsiFallbackOverlay(
        GeographicRectangle geographicBounds)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            GeographicBounds: geographicBounds,
            MaxTextureSize: MaxTextureSize,
            Sources: CreatePlateauOrthoWithGsiFallbackSources(),
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
    }

    public static TerrainTextureSource[] CreatePlateauOrthoWithGsiFallbackSources()
    {
        return
        [
            new TerrainTextureTileSource(PlateauOrthoUrlTemplate, PlateauOrthoZoomLevel),
            new TerrainTextureTileSource(PlateauOrthoUrlTemplate, FallbackZoomLevel),
            new TerrainTextureTileSource(GsiFallbackUrlTemplate, FallbackZoomLevel),
        ];
    }

    public static bool IsGsiFallbackSource(TerrainTextureSource source)
    {
        return source is TerrainTextureTileSource tileSource
            && string.Equals(tileSource.UrlTemplate, GsiFallbackUrlTemplate, StringComparison.Ordinal)
            && tileSource.ZoomLevel == FallbackZoomLevel;
    }
}
