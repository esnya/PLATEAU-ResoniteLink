using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunStartRequest(
    ResoniteSceneSetupInfo SetupInfo,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials,
    PlateauImportRequest NormalizedRequest,
    ResoniteLocalOrigin RequestLocalOrigin,
    ResoniteImportMemoryProfile MemoryProfile,
    int ConnectionCount,
    bool MeshBakeEnabled);

internal sealed record LiveSendRunStartContext(
    Uri Endpoint,
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);
