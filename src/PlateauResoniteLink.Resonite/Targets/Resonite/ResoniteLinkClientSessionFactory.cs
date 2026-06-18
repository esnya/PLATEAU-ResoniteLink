using System;


using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal interface IResoniteClientSessionFactory
{
    ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics);
}

internal sealed class ResoniteLinkClientSessionFactory(
    Func<IResoniteLinkClient> baseClientFactory) : IResoniteClientSessionFactory
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
            baseClientFactory);
    }
}
