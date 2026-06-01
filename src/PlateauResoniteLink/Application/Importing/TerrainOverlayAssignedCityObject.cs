using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal abstract record TerrainOverlayAssignedCityObject
{
    private protected TerrainOverlayAssignedCityObject(ParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        CityObject = cityObject;
    }

    public ParsedCityObject CityObject { get; }

    public sealed record WithoutOverlay : TerrainOverlayAssignedCityObject
    {
        public WithoutOverlay(ParsedCityObject cityObject)
            : base(cityObject)
        {
        }
    }

    public sealed record WithOverlay : TerrainOverlayAssignedCityObject
    {
        public WithOverlay(ParsedCityObject cityObject, TerrainTextureOverlay overlay)
            : base(cityObject)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            Overlay = overlay;
        }

        public TerrainTextureOverlay Overlay { get; }
    }
}
