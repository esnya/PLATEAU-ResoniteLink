using System.Threading;
using System.Threading.Tasks;


namespace PlateauResoniteLink.Application.Importing.CityGml;

internal interface ICityGmlDocumentReader
{
    Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default);
}
