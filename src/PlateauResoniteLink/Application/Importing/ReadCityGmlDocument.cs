using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal delegate Task<ImportedSceneSourceSnapshot> ReadCityGmlDocument(
    ResolvedLocalPlateauImportRequest request,
    ILogger? logger = null,
    CancellationToken cancellationToken = default);
