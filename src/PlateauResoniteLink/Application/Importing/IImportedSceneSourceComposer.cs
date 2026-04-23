using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        PlateauImportRequest request,
        LocalCityGmlBootstrapSnapshot readResult,
        Action<string>? progressReporter = null,
        IImportedCityObjectOptimizer? cityObjectOptimizer = null);
}
