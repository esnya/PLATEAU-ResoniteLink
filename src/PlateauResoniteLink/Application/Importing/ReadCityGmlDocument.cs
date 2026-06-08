using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal delegate Task<ImportedSceneSourceSnapshot> ReadCityGmlDocument(
    ResolvedLocalPlateauImportRequest request,
    Action<string>? progressReporter = null,
    CancellationToken cancellationToken = default);
