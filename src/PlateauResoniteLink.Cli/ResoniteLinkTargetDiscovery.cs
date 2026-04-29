using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ResoniteLink;

namespace PlateauResoniteLink.Cli;

internal interface IResoniteLinkTargetDiscovery
{
    Task<IReadOnlyList<ResoniteLinkTarget>> DiscoverAsync(CancellationToken cancellationToken = default);
}

internal sealed record ResoniteLinkTarget(
    string SessionName,
    string SessionId,
    Uri Endpoint,
    DateTime LastUpdateTimestamp);

internal sealed class ResoniteLinkTargetDiscovery : IResoniteLinkTargetDiscovery
{
    private static readonly TimeSpan DiscoveryWindow = TimeSpan.FromMilliseconds(750);

    public async Task<IReadOnlyList<ResoniteLinkTarget>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        using LinkSessionListener listener = new();
        listener.Start();
        await Task.Delay(DiscoveryWindow, cancellationToken);

        List<ResoniteLinkSession> sessions = [];
        listener.GetDiscoveredSessions(sessions);

        return sessions
            .Where(static session => session.LinkPort is >= 1 and <= 65535)
            .Select(static session => new ResoniteLinkTarget(
                session.SessionName ?? string.Empty,
                session.SessionId ?? string.Empty,
                new Uri($"ws://localhost:{session.LinkPort}/", UriKind.Absolute),
                session.LastUpdateTimestamp))
            .GroupBy(static target => target.Endpoint, UriComparer.Instance)
            .Select(static group => group
                .OrderByDescending(static target => target.LastUpdateTimestamp)
                .First())
            .OrderBy(static target => target.SessionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static target => target.Endpoint.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class UriComparer : IEqualityComparer<Uri>
    {
        public static UriComparer Instance { get; } = new();

        public bool Equals(Uri? x, Uri? y)
        {
            return string.Equals(x?.AbsoluteUri, y?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(Uri obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AbsoluteUri);
        }
    }
}
