using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal delegate Task<IImportedSceneSource> CreateImportedSceneSource(
    ResolvedLocalPlateauImportRequest request,
    ILoggerFactory? loggerFactory = null,
    CancellationToken cancellationToken = default);
