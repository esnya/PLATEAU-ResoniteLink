using System.Threading;
using System.Threading.Tasks;


namespace PlateauResoniteLink.Application.Importing;

internal interface ICityGmlDocumentReader
{
    Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default);
}
