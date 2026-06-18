using System.Collections.Generic;

using PlateauResoniteLink.Plateau.Application.Importing.Source;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal sealed class ImportedSceneSourceContext
{
    internal ImportedSceneSourceContext(
        IReadOnlyList<SourceFilePipeline> sourceFilePipelines,
        GeodeticPoint globalOriginPoint)
    {
        SourceFilePipelines = sourceFilePipelines;
        GlobalOriginPoint = globalOriginPoint;
    }

    internal IReadOnlyList<SourceFilePipeline> SourceFilePipelines { get; }

    internal GeodeticPoint GlobalOriginPoint { get; }
}
