using System;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal static class ResoniteLinkTransportSessionFactory
{
    internal static ILiveSendClientSession Create(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        ILogger logger,
        Func<ILogger, IResoniteLinkClient> baseClientFactory)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(baseClientFactory);

        IResoniteLinkClient CreateConfiguredClient()
        {
            IResoniteLinkClient client = new RetryingResoniteLinkClient(
                () => baseClientFactory(logger),
                logger);
            return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
        }

        return new LiveSendClientSession(
            CreateConfiguredClient,
            endpoint,
            connectionCount,
            diagnostics,
            logger);
    }
}
