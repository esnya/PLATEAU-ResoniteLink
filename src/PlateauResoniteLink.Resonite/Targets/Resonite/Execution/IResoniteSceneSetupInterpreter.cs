using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

internal interface IResoniteSceneSetupInterpreter
{
    Task<ResoniteSceneSetupState> SetupAsync(
        IResoniteLinkClient setupClient,
        ResoniteSceneSetupInfo setupInfo,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        CancellationToken cancellationToken);
}
