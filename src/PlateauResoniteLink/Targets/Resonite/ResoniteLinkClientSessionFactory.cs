using System;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteClientSessionFactory
{
    ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics);
}

internal sealed class ResoniteLinkClientSessionFactory(
    Func<ILogger, IResoniteLinkClient> baseClientFactory) : IResoniteClientSessionFactory
{
    public ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return ResoniteLinkTransportSessionFactory.Create(
            options.Endpoint,
            options.ConnectionCount,
            diagnostics,
            options.LoggerFactory.CreateLogger("PlateauResoniteLink.ResoniteLink"),
            baseClientFactory);
    }
}
