using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneSetupInterpreter
{
    Task<ResoniteSceneSetupState> SetupAsync(
        IResoniteLinkClient setupClient,
        ResoniteSceneSetupInfo setupInfo,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken);
}
