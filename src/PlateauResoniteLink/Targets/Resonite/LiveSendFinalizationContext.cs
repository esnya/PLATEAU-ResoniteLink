using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendFinalizationContext(
    Uri Endpoint,
    LiveSendEnqueueContext EnqueueContext,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);
