using System;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal static class ResoniteLinkTransportSessionFactory
{
    internal static ILiveSendClientSession Create(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        Func<Action<string>?, IResoniteLinkClient> baseClientFactory)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(baseClientFactory);

        IResoniteLinkClient CreateConfiguredClient()
        {
            IResoniteLinkClient client = new RetryingResoniteLinkClient(
                () => baseClientFactory(progressReporter),
                progressReporter);
            return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
        }

        return new LiveSendClientSession(
            CreateConfiguredClient,
            endpoint,
            connectionCount,
            diagnostics,
            progressReporter);
    }
}
