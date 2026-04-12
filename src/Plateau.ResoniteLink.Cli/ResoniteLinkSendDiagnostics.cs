using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using Plateau.ResoniteLink.Application.Logging;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteLinkSendDiagnostics
{
    private static readonly Meter Meter = new("Plateau.ResoniteLink.LiveSend");
    private static readonly Counter<long> CityObjectCounter = Meter.CreateCounter<long>(
        "plateau.resonitelink.live.city_objects",
        unit: "objects");
    private static readonly Counter<long> RpcCallCounter = Meter.CreateCounter<long>(
        "plateau.resonitelink.live.rpc_calls",
        unit: "operations");
    private static readonly Histogram<double> PrepareDurationHistogram = Meter.CreateHistogram<double>(
        "plateau.resonitelink.live.prepare.duration",
        unit: "s");
    private static readonly Histogram<double> SendDurationHistogram = Meter.CreateHistogram<double>(
        "plateau.resonitelink.live.send.duration",
        unit: "s");
    private static readonly Histogram<double> SendWindowDurationHistogram = Meter.CreateHistogram<double>(
        "plateau.resonitelink.live.send_window.duration",
        unit: "s");
    private static readonly Histogram<long> RpcPerCityObjectHistogram = Meter.CreateHistogram<long>(
        "plateau.resonitelink.live.rpc_per_city_object",
        unit: "operations");

    private readonly Action<string>? reporter;
    private readonly AsyncLocal<CityObjectSendScope?> currentScope = new();
    private readonly ConcurrentDictionary<string, long> rpcCallsByOperation = new(StringComparer.Ordinal);
    private long sentCityObjectCount;
    private long skippedMeshImportFailureCityObjectCount;
    private long totalRpcCalls;
    private long totalRpcCallsForSentObjects;
    private double totalPrepareDurationSeconds;
    private double totalSendDurationSeconds;
    private Stopwatch? sendWindowStopwatch;

    private ResoniteLinkSendDiagnostics(bool enabled, Action<string>? reporter = null)
    {
        Enabled = enabled;
        this.reporter = reporter;
    }

    public static ResoniteLinkSendDiagnostics Disabled { get; } = new(enabled: false);

    public bool Enabled { get; }

    public static ResoniteLinkSendDiagnostics CreateEnabled(Action<string>? reporter = null)
    {
        return new ResoniteLinkSendDiagnostics(enabled: true, reporter);
    }

    public void StartSendWindow(int connectionCount)
    {
        if (!Enabled)
        {
            return;
        }

        sendWindowStopwatch = Stopwatch.StartNew();
        reporter?.Invoke(
            PlateauLog.Info("live-metrics", $"Enabled live send metrics via System.Diagnostics.Metrics (connections={connectionCount})."));
    }

    public void CompleteSendWindow()
    {
        if (!Enabled || sendWindowStopwatch is null)
        {
            return;
        }

        sendWindowStopwatch.Stop();
        double elapsedSeconds = sendWindowStopwatch.Elapsed.TotalSeconds;
        SendWindowDurationHistogram.Record(elapsedSeconds);

        long sentCount = Interlocked.Read(ref sentCityObjectCount);
        long skippedMeshImportFailureCount = Interlocked.Read(ref skippedMeshImportFailureCityObjectCount);
        long rpcCalls = Interlocked.Read(ref totalRpcCalls);
        long rpcCallsForSentObjects = Interlocked.Read(ref totalRpcCallsForSentObjects);
        double prepareSeconds = Interlocked.CompareExchange(ref totalPrepareDurationSeconds, 0.0, 0.0);
        double sendSeconds = Interlocked.CompareExchange(ref totalSendDurationSeconds, 0.0, 0.0);
        double throughput = elapsedSeconds > 1e-9 ? sentCount / elapsedSeconds : 0.0;
        double averagePrepareSeconds = sentCount > 0 ? prepareSeconds / sentCount : 0.0;
        double averageSendSeconds = sentCount > 0 ? sendSeconds / sentCount : 0.0;
        double averageRpcPerSentCityObject = sentCount > 0 ? (double)rpcCallsForSentObjects / sentCount : 0.0;
        string rpcSummary = string.Join(
            ", ",
            rpcCallsByOperation
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}={pair.Value}"));

        reporter?.Invoke(
            PlateauLog.Info(
                "live-metrics",
                $"send_window_s={elapsedSeconds:F3} sent={sentCount} skipped_mesh_import_failure={skippedMeshImportFailureCount} "
                + $"throughput_obj_per_s={throughput:F2} avg_prepare_s={averagePrepareSeconds:F4} "
                + $"avg_send_s={averageSendSeconds:F4} avg_rpc_per_sent={averageRpcPerSentCityObject:F2} total_rpc={rpcCalls}"));

        if (!string.IsNullOrWhiteSpace(rpcSummary))
        {
            reporter?.Invoke(PlateauLog.Debug("live-metrics", $"rpc_breakdown {rpcSummary}"));
        }
    }

    public void RecordPrepare(string packageName, double elapsedSeconds)
    {
        if (!Enabled)
        {
            return;
        }

        PrepareDurationHistogram.Record(
            elapsedSeconds,
            new TagList
            {
                { "package", packageName },
            });
        AddDouble(ref totalPrepareDurationSeconds, elapsedSeconds);
    }

    public CityObjectSendScope BeginCityObjectSend(string packageName)
    {
        if (!Enabled)
        {
            return CityObjectSendScope.Disabled;
        }

        CityObjectSendScope scope = new(this, packageName);
        currentScope.Value = scope;
        return scope;
    }

    public void RecordRpcCall(string operation)
    {
        if (!Enabled)
        {
            return;
        }

        RpcCallCounter.Add(
            1,
            new TagList
            {
                { "operation", operation },
            });
        rpcCallsByOperation.AddOrUpdate(operation, 1, static (_, count) => count + 1);
        Interlocked.Increment(ref totalRpcCalls);
        currentScope.Value?.IncrementRpc();
    }

    private void CompleteCityObjectSend(string packageName, string outcome, double elapsedSeconds, long rpcCount)
    {
        SendDurationHistogram.Record(
            elapsedSeconds,
            new TagList
            {
                { "package", packageName },
                { "outcome", outcome },
            });
        CityObjectCounter.Add(
            1,
            new TagList
            {
                { "package", packageName },
                { "outcome", outcome },
            });

        if (string.Equals(outcome, "sent", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref sentCityObjectCount);
            Interlocked.Add(ref totalRpcCallsForSentObjects, rpcCount);
            RpcPerCityObjectHistogram.Record(
                rpcCount,
                new TagList
                {
                    { "package", packageName },
                });
        }
        else if (string.Equals(outcome, "skipped_mesh_import_failure", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref skippedMeshImportFailureCityObjectCount);
        }
        AddDouble(ref totalSendDurationSeconds, elapsedSeconds);
    }

    private static void AddDouble(ref double target, double value)
    {
        double initialValue;
        double computedValue;

        do
        {
            initialValue = target;
            computedValue = initialValue + value;
        }
        while (Math.Abs(Interlocked.CompareExchange(ref target, computedValue, initialValue) - initialValue) > double.Epsilon);
    }

    internal sealed class CityObjectSendScope : IDisposable
    {
        private readonly ResoniteLinkSendDiagnostics? owner;
        private readonly Stopwatch? stopwatch;
        private readonly string? packageName;
        private bool completed;

        private CityObjectSendScope()
        {
        }

        internal CityObjectSendScope(ResoniteLinkSendDiagnostics owner, string packageName)
        {
            this.owner = owner;
            stopwatch = Stopwatch.StartNew();
            this.packageName = packageName;
        }

        public static CityObjectSendScope Disabled { get; } = new();

        public long RpcCount { get; private set; }

        public void IncrementRpc()
        {
            RpcCount++;
        }

        public void MarkSent()
        {
            Complete("sent");
        }

        public void MarkSkippedMeshImportFailure()
        {
            Complete("skipped_mesh_import_failure");
        }

        public void Dispose()
        {
            if (owner is not null)
            {
                owner.currentScope.Value = null;
            }
        }

        private void Complete(string outcome)
        {
            if (completed || owner is null || stopwatch is null)
            {
                return;
            }

            completed = true;
            stopwatch.Stop();
            owner.CompleteCityObjectSend(packageName ?? string.Empty, outcome, stopwatch.Elapsed.TotalSeconds, RpcCount);
        }
    }
}
