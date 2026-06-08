using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal delegate Task<ResoniteSceneSetupState> SetupResoniteScene(
    IResoniteLinkClient setupClient,
    ResoniteSceneSetupInfo setupInfo,
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
    CancellationToken cancellationToken);
