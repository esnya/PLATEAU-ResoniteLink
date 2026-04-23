using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteLinkTransportSessionFactory
{
    internal static ILiveSendClientSession Create(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        Func<IResoniteLinkClient> baseClientFactory)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(baseClientFactory);

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
    private readonly Func<IResoniteLinkClient> baseClientFactory;

    public ResoniteLinkClientSessionFactory(Func<IResoniteLinkClient> baseClientFactory)
    {
        this.baseClientFactory = baseClientFactory ?? throw new ArgumentNullException(nameof(baseClientFactory));
    }

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
            options.ProgressReporter,
            baseClientFactory);
    }
}
