using System.Diagnostics.CodeAnalysis;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class RetryingResoniteLinkClient(
    Func<IResoniteLinkClient> clientFactory,
    Action<string>? reporter = null) : IResoniteLinkClient
{
    private const int AttemptLimit = 2;
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
                    $"[live][warn] ResoniteLink connect failed on attempt {attempt}/{AttemptLimit}. "
                    + $"Creating a fresh client before retry. Reason: {exception.Message}");
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

    public Task RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.RunDataModelOperationBatchAsync(state, ct),
            operations,
            "RunDataModelOperationBatch",
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
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.ImportMeshAsync(state, ct),
            request,
            "ImportMesh",
            cancellationToken);
    }

    public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.ImportTextureAsync(state, ct),
            textureImport,
            "ImportTexture",
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
                $"[live][warn] ResoniteLink {operationName} failed without retry. "
                + $"Reason: {exception.Message}");
            throw;
        }
        finally
        {
            operationGate.Release();
        }
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
                        $"[live][warn] ResoniteLink {operationName} failed on attempt {attempt}/{AttemptLimit}. "
                        + $"Reconnecting before retry. Reason: {exception.Message}");
                    await ReconnectAsync(observedGeneration, cancellationToken);
                }
            }

            throw lastException!;
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

            IResoniteLinkClient previous = inner;
            IResoniteLinkClient replacement = clientFactory();

            try
            {
                await replacement.ConnectAsync(endpoint, cancellationToken);
            }
            catch
            {
                replacement.Dispose();
                throw;
            }

            inner = replacement;
            Interlocked.Increment(ref generation);
            previous.Dispose();
            reporter?.Invoke($"[live][warn] Reconnected ResoniteLink client to {endpoint}.");
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
