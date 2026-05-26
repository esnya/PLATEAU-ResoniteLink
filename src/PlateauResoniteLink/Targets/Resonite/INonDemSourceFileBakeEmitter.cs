using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemSourceFileBakeEmitter
{
    Task<int> EmitAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemBufferedCityObject> cityObjects,
        int batchStartIndex,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken);
}
