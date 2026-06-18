using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing.CityGml;

internal sealed class CityGmlImportedSceneSourceReader(ICityGmlDocumentReader documentReader) : IImportedSceneSourceReader
{
    private readonly ICityGmlDocumentReader documentReader =
        documentReader ?? throw new ArgumentNullException(nameof(documentReader));

    public Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return documentReader.ReadAsync(request, cancellationToken);
    }
}
