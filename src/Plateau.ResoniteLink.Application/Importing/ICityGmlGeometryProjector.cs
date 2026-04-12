using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlGeometryProjector
{
    IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        LocalCityGmlGeometryProjectionContext projectionContext,
        PlateauImportRequest request);
}
