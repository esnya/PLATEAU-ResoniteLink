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

internal delegate Task<CreatedSlot?> ResolveResoniteDatasetRootSlot(
    IResoniteLinkClient setupClient,
    string datasetRootName,
    CancellationToken cancellationToken);

internal delegate Task<SceneAnchor> ResolveResoniteSceneAnchor(
    IResoniteLinkClient setupClient,
    ResoniteSlotLocator datasetRootSlot,
    string completionMeshCode,
    CancellationToken cancellationToken);

internal delegate Task<CreatedSlot> CreateResoniteSlot(
    IResoniteLinkClient client,
    ResoniteSlotLocator parent,
    string slotName,
    ResoniteFloat3? position,
    ResoniteFloatQ? rotation,
    CancellationToken cancellationToken);
