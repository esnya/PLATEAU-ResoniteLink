using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneBootstrapInterpreter
{
    Task<ResoniteSceneBootstrapState> BootstrapAsync(
        IResoniteLinkClient setupClient,
        ResoniteSceneBootstrapInfo setupInfo,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken);
}
