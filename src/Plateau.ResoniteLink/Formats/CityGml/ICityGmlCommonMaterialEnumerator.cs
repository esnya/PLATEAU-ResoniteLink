using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlCommonMaterialEnumerator
{
    IEnumerable<ResoniteMaterialBinding> EnumerateCommonMaterials(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        PlateauImportRequest request,
        ISet<string> emittedMaterialKeys);
}
