using System;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal delegate IResoniteLinkClient CreateResoniteLinkClient(ResoniteLinkClientCreationContext context);

internal sealed record ResoniteLinkClientCreationContext(Action<string>? ProgressReporter);
