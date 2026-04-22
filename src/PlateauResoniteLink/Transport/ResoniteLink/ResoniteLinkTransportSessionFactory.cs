using System;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal static class ResoniteLinkTransportSessionFactory
{
    internal static ILiveSendClientSession Create(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        Func<IResoniteLinkClient>? baseClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(diagnostics);

        baseClientFactory ??= static () => new ResoniteLinkClient();

        IResoniteLinkClient CreateConfiguredClient()
        {
            IResoniteLinkClient client = new RetryingResoniteLinkClient(baseClientFactory, progressReporter);
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

internal sealed class ResoniteLinkClientSessionFactory : IResoniteClientSessionFactory
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
            options.ProgressReporter);
    }
}
