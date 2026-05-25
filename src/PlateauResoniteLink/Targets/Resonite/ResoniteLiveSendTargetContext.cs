using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSendTargetContext(
    Uri Endpoint,
    int ConnectionCount,
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);
