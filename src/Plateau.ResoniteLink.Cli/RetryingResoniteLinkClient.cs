using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class RetryingResoniteLinkClient(
    Func<IResoniteLinkClient> clientFactory,
    Action<string>? reporter = null,
    int importMeshTimeoutMilliseconds = CliDefaultOptions.ResoniteLinkImportMeshTimeoutMilliseconds) : IResoniteLinkClient
{
    private const int AttemptLimit = 2;
    private static readonly TimeSpan SlowBatchThreshold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SlowImportThreshold = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly SemaphoreSlim reconnectGate = new(1, 1);
    private IResoniteLinkClient inner = clientFactory();
    private Uri? endpoint;
    private int generation;

    public void Dispose()
    {
        operationGate.Dispose();
        reconnectGate.Dispose();
        inner.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Connect retries intentionally handle arbitrary transport failures before recreating the client.")]
    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        this.endpoint = endpoint;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= AttemptLimit; attempt++)
        {
            try
            {
                await inner.ConnectAsync(endpoint, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt >= AttemptLimit)
                {
                    break;
                }

                reporter?.Invoke(
                    PlateauLog.Warning(
                        "live",
                        $"ResoniteLink connect failed on attempt {attempt}/{AttemptLimit}. "
                        + $"Creating a fresh client before retry. Reason: {exception.Message}"));
                ReplaceClientWithoutConnecting();
            }
        }

        throw lastException!;
    }

    public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.AddComponentAsync(state, ct),
            request,
            "AddComponent",
            cancellationToken);
    }

    public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.AddSlotAsync(state, ct),
            request,
            "AddSlot",
            cancellationToken);
    }

    public Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        return ExecuteTimedWithoutReconnectAsync(
            static (client, state, ct) => client.RunDataModelOperationBatchAsync(state, ct),
            operations,
            "RunDataModelOperationBatch",
            SlowBatchThreshold,
            cancellationToken);
    }

    public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        return ExecuteWithReconnectAsync(
            static (client, state, ct) => client.GetComponentAsync(state, ct),
            componentId,
            "GetComponent",
            cancellationToken);
    }

    public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
    {
        return ExecuteWithReconnectAsync(
            static (client, state, ct) => client.GetSlotAsync(state.SlotId, state.Depth, ct),
            (SlotId: slotId, Depth: depth),
            "GetSlot",
            cancellationToken);
    }

    public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        _ = importMeshTimeoutMilliseconds;
        return ExecuteTimedImportAsync(
            static (client, state, ct) => client.ImportMeshAsync(state, ct),
            request,
            "ImportMesh",
            SlowImportThreshold,
            cancellationToken);
    }

    public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        return ExecuteTimedImportAsync(
            static (client, state, ct) => client.ImportTextureAsync(state, ct),
            textureImport,
            "ImportTexture",
            SlowImportThreshold,
            cancellationToken);
    }

    public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.UpdateComponentAsync(state, ct),
            request,
            "UpdateComponent",
            cancellationToken);
    }

    private async Task ExecuteWithReconnectAsync<TState>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken)
    {
        await ExecuteWithReconnectAsync<TState, object?>(
            async (client, innerState, ct) =>
            {
                await operation(client, innerState, ct);
                return null;
            },
            state,
            operationName,
            cancellationToken);
    }

    private async Task ExecuteWithoutReconnectAsync<TState>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken)
    {
        await ExecuteWithoutReconnectAsync<TState, object?>(
            async (client, innerState, ct) =>
            {
                await operation(client, innerState, ct);
                return null;
            },
            state,
            operationName,
            cancellationToken);
    }

    private async Task<TResult> ExecuteWithoutReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation(inner, state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            reporter?.Invoke(
                PlateauLog.Warning(
                    "live",
                    $"ResoniteLink {operationName} failed without retry. Reason: {exception.Message}"));
            throw ResoniteLinkOperationException.Wrap(operationName, exception);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<TResult> ExecuteTimedWithoutReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        TimeSpan slowThreshold,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TResult result = await ExecuteWithoutReconnectAsync(
            operation,
            state,
            operationName,
            cancellationToken);
        stopwatch.Stop();

        reporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"ResoniteLink {operationName} completed in {stopwatch.Elapsed.TotalSeconds:F3}s."));
        if (stopwatch.Elapsed >= slowThreshold)
        {
            reporter?.Invoke(
                PlateauLog.Warning(
                    "live",
                    $"ResoniteLink {operationName} exceeded slow threshold {slowThreshold.TotalSeconds:F1}s "
                    + $"(actual={stopwatch.Elapsed.TotalSeconds:F3}s)."));
        }

        return result;
    }

    private async Task<TResult> ExecuteTimedImportAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        TimeSpan slowThreshold,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TResult result = await ExecuteImportAsync(
            operation,
            state,
            operationName,
            cancellationToken);
        stopwatch.Stop();

        reporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"ResoniteLink {operationName} completed in {stopwatch.Elapsed.TotalSeconds:F3}s."));
        if (stopwatch.Elapsed >= slowThreshold)
        {
            reporter?.Invoke(
                PlateauLog.Warning(
                    "live",
                    $"ResoniteLink {operationName} exceeded slow threshold {slowThreshold.TotalSeconds:F1}s "
                    + $"(actual={stopwatch.Elapsed.TotalSeconds:F3}s)."));
        }

        return result;
    }

    private async Task<TResult> ExecuteImportAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithoutReconnectAsync(
            operation,
            state,
            operationName,
            cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Reconnect retries intentionally handle arbitrary ResoniteLink failures before replacing the client.")]
    private async Task<TResult> ExecuteWithReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= AttemptLimit; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int observedGeneration = Volatile.Read(ref generation);

                try
                {
                    return await operation(inner, state, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    if (attempt >= AttemptLimit)
                    {
                        break;
                    }

                    reporter?.Invoke(
                        PlateauLog.Warning(
                            "live",
                            $"ResoniteLink {operationName} failed on attempt {attempt}/{AttemptLimit}. "
                            + $"Reconnecting before retry. Reason: {exception.Message}"));
                    await ReconnectAsync(observedGeneration, cancellationToken);
                }
            }

            throw ResoniteLinkOperationException.Wrap(operationName, lastException!);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task ReconnectAsync(int observedGeneration, CancellationToken cancellationToken)
    {
        await reconnectGate.WaitAsync(cancellationToken);
        try
        {
            if (observedGeneration != generation)
            {
                return;
            }

            if (endpoint is null)
            {
                throw new InvalidOperationException("Cannot reconnect before an endpoint has been established.");
            }

            IResoniteLinkClient? replacement = null;
            IResoniteLinkClient? previous = null;
            try
            {
                previous = inner;
                replacement = clientFactory();

                await replacement.ConnectAsync(endpoint, cancellationToken);

                inner = replacement;
                replacement = null;
                Interlocked.Increment(ref generation);

                previous.Dispose();
                previous = null;
                reporter?.Invoke(PlateauLog.Warning("live", $"Reconnected ResoniteLink client to {endpoint}."));
            }
            finally
            {
                replacement?.Dispose();
                previous?.Dispose();
            }
        }
        finally
        {
            reconnectGate.Release();
        }
    }

    private void ReplaceClientWithoutConnecting()
    {
        IResoniteLinkClient previous = inner;
        inner = clientFactory();
        Interlocked.Increment(ref generation);
        previous.Dispose();
    }
}

internal sealed class ResoniteLinkOperationException : InvalidOperationException
{
    public ResoniteLinkOperationException(string operationName, string message, Exception innerException)
        : base(message, innerException)
    {
        OperationName = operationName;
    }

    public string OperationName { get; }

    public static ResoniteLinkOperationException Wrap(string operationName, Exception exception)
    {
        return exception as ResoniteLinkOperationException
            ?? new ResoniteLinkOperationException(operationName, exception.Message, exception);
    }
}
