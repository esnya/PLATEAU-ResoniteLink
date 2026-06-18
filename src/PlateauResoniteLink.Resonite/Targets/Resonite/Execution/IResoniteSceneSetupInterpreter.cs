using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;
using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneSetupInterpreter
{
    Task<ResoniteSceneSetupState> SetupAsync(
        IResoniteLinkClient setupClient,
        ResoniteSceneSetupInfo setupInfo,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        CancellationToken cancellationToken);
}
