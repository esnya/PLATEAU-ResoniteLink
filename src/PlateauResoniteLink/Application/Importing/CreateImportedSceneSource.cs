using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal delegate Task<IImportedSceneSource> CreateImportedSceneSource(
    ResolvedLocalPlateauImportRequest request,
    Action<string>? progressReporter = null,
    CancellationToken cancellationToken = default);
