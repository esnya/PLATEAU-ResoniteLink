using System;

using PlateauResoniteLink.Application.Importing.CityGml;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceComposer(
    IImportedSceneMetadataComposer metadataComposer,
    ICityGmlGeometryProjector geometryProjector,
    IDemTextureSourcePolicy demTextureSourcePolicy) : IImportedSceneSourceComposer
{
    private readonly IImportedSceneMetadataComposer metadataComposer =
        metadataComposer ?? throw new ArgumentNullException(nameof(metadataComposer));
    private readonly ICityGmlGeometryProjector geometryProjector =
        geometryProjector ?? throw new ArgumentNullException(nameof(geometryProjector));
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy =
        demTextureSourcePolicy ?? throw new ArgumentNullException(nameof(demTextureSourcePolicy));

    public IImportedSceneSource Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        IImportedObjectUnitOptimizer objectUnitOptimizer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        PlateauImportRequest importRequest = request.ToImportRequest();

        ImportedSceneMetadata metadata = metadataComposer.Compose(request, readResult);

        return new StreamingImportedSceneSource(
            metadata,
            importRequest,
            readResult,
            geometryProjector,
            demTextureSourcePolicy,
            objectUnitOptimizer);
    }
}
