using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendWorkerContext(
    Uri Endpoint,
    int ConnectionCount,
    Func<IResoniteLinkClient> GetRoutedClient,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);
