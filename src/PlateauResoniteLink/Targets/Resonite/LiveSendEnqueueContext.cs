using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendEnqueueContext(
    int ConnectionCount,
    Func<IResoniteLinkClient> GetRoutedClient,
    Action<string>? ProgressReporter);
