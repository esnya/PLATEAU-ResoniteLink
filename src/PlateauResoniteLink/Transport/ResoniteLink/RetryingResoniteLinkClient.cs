using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using ResoniteLink;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal sealed class RetryingResoniteLinkClient : IResoniteLinkClient
{
    private const int AttemptLimit = 2;

    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly ILogger logger;
    private readonly SemaphoreSlim reconnectGate = new(1, 1);
    private readonly ConcurrentBag<ClientState> knownClients = [];
    private ClientState currentClient;
    private Uri? endpoint;
    private int disposed;

    public RetryingResoniteLinkClient(
        Func<IResoniteLinkClient> clientFactory,
        ILogger? logger = null)
    {
        this.clientFactory = clientFactory;
        this.logger = logger ?? NullLogger.Instance;

        currentClient = new ClientState(clientFactory());
        knownClients.Add(currentClient);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        reconnectGate.Dispose();
        foreach (ClientState client in knownClients)
        {
            client.MarkRetired();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Connect retries intentionally handle arbitrary transport failures before recreating the client.")]
    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(endpoint);
        this.endpoint = endpoint;

        Exception? lastException = null;
        for (int attempt = 1; attempt <= AttemptLimit; attempt++)
        {
            ClientState observedClient = Volatile.Read(ref currentClient);
            try
            {
                await observedClient.Client.ConnectAsync(endpoint, cancellationToken);
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

                logger.WriteWarning(
                    "ResoniteLink connect failed on attempt {Attempt}/{AttemptLimit}. Creating a fresh client before retry. Reason: {Reason}",
                    attempt,
                    AttemptLimit,
                    exception.Message);
                ReplaceClientWithoutConnecting(observedClient);
            }
        }

        throw lastException!;
    }

    public Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        return ExecuteMeasuredWithoutReconnectAsync(
            static (client, state, ct) => client.RunDataModelOperationBatchAsync(state, ct),
            operations,
            "RunDataModelOperationBatch",
            cancellationToken,
            CreateBatchOperationDescriptor);
    }

    public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.AddComponentAsync(state, ct),
            request,
            "AddComponent",
            cancellationToken,
            static state => CreateComponentOperationDescriptor(state.Data));
    }

    public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.AddSlotAsync(state, ct),
            request,
            "AddSlot",
            cancellationToken,
            static state => CreateSlotOperationDescriptor(state.Data));
    }

    public Task<Component?> GetComponentAsync(ResoniteTransportComponentLocator component, CancellationToken cancellationToken)
    {
        return ExecuteWithReconnectAsync(
            static (client, state, ct) => client.GetComponentAsync(state, ct),
            component,
            "GetComponent",
            cancellationToken,
            static state => $"component '{state.Value}'");
    }

    public Task<Slot?> GetSlotAsync(ResoniteTransportSlotLocator slot, int depth, CancellationToken cancellationToken)
    {
        return ExecuteWithReconnectAsync(
            static (client, state, ct) => client.GetSlotAsync(state.Slot, state.Depth, ct),
            (Slot: slot, Depth: depth),
            "GetSlot",
            cancellationToken,
            static state => $"slot '{state.Slot.Value}'");
    }

    public Task<Uri> ImportMeshAsync(IGeometryImportSource geometrySource, CancellationToken cancellationToken)
    {
        return ExecuteMeasuredWithoutReconnectAsync(
            static (client, state, ct) => client.ImportMeshAsync(state, ct),
            geometrySource,
            "ImportMesh",
            cancellationToken,
            static _ => null);
    }

    public Task<Uri> ImportTextureAsync(ITextureImportSource textureSource, CancellationToken cancellationToken)
    {
        return ExecuteMeasuredWithReconnectAsync(
            static (client, state, ct) => client.ImportTextureAsync(state, ct),
            textureSource,
            "ImportTexture",
            cancellationToken,
            static _ => null,
            retryAfterFatalProtocolFailure: true);
    }

    public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
    {
        return ExecuteWithoutReconnectAsync(
            static (client, state, ct) => client.UpdateComponentAsync(state, ct),
            request,
            "UpdateComponent",
            cancellationToken,
            static state => $"component '{state.Component.Value}'");
    }

    private async Task ExecuteWithReconnectAsync<TState>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken,
        Func<TState, string?>? occupancyDescriptorProvider = null)
    {
        await ExecuteWithReconnectAsync<TState, object?>(
            async (client, innerState, ct) =>
            {
                await operation(client, innerState, ct);
                return null;
            },
            state,
            operationName,
            cancellationToken,
            occupancyDescriptorProvider);
    }

    private async Task ExecuteWithoutReconnectAsync<TState>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken,
        Func<TState, string?>? occupancyDescriptorProvider = null)
    {
        await ExecuteWithoutReconnectAsync<TState, object?>(
            async (client, innerState, ct) =>
            {
                await operation(client, innerState, ct);
                return null;
            },
            state,
            operationName,
            cancellationToken,
            occupancyDescriptorProvider);
    }

    private async Task<TResult> ExecuteWithoutReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken,
        Func<TState, string?>? occupancyDescriptorProvider = null)
    {
        ClientState observedClient;
        using ClientLease lease = AcquireCurrentClient(out observedClient);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation(lease.Client, state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (ShouldRetireClientAfterFailure(exception))
            {
                await ReplaceClientAfterFatalFailureAsync(observedClient, operationName, cancellationToken);
                logger.WriteWarning(
                    "ResoniteLink {OperationName} retired the active client after a fatal protocol failure. Reason: {Reason}",
                    operationName,
                    exception.Message);
            }

            logger.WriteWarning(
                "ResoniteLink {OperationName} failed without retry. Reason: {Reason}",
                operationName,
                exception.Message);
            throw ResoniteLinkOperationException.Wrap(operationName, exception);
        }
    }

    private async Task<TResult> ExecuteMeasuredWithoutReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken,
        Func<TState, string?>? occupancyDescriptorProvider = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TResult result = await ExecuteWithoutReconnectAsync(
            operation,
            state,
            operationName,
            cancellationToken,
            occupancyDescriptorProvider);
        stopwatch.Stop();

        logger.WriteDebug(
            "ResoniteLink {OperationName} completed in {ElapsedSeconds:F3}s.",
            operationName,
            stopwatch.Elapsed.TotalSeconds);

        return result;
    }

    private async Task<TResult> ExecuteMeasuredWithReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken,
        Func<TState, string?>? occupancyDescriptorProvider = null,
        bool retryAfterFatalProtocolFailure = false)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TResult result = await ExecuteWithReconnectAsync(
            operation,
            state,
            operationName,
            cancellationToken,
            occupancyDescriptorProvider,
            retryAfterFatalProtocolFailure);
        stopwatch.Stop();

        logger.WriteDebug(
            "ResoniteLink {OperationName} completed in {ElapsedSeconds:F3}s.",
            operationName,
            stopwatch.Elapsed.TotalSeconds);

        return result;
    }

    private static string? CreateSlotOperationDescriptor(Slot? slot)
    {
        if (!string.IsNullOrWhiteSpace(slot?.Name?.Value))
        {
            return $"slot '{slot.Name!.Value}'";
        }

        if (!string.IsNullOrWhiteSpace(slot?.ID))
        {
            return $"slot '{slot.ID}'";
        }

        return null;
    }

    private static string? CreateComponentOperationDescriptor(Component? component)
    {
        return string.IsNullOrWhiteSpace(component?.ComponentType)
            ? null
            : $"component '{component.ComponentType}'";
    }

    private static string? CreateBatchOperationDescriptor(IReadOnlyList<DataModelOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        return operations.Count switch
        {
            0 => "empty batch",
            1 => "batch with 1 operation",
            _ => $"batch with {operations.Count} operations",
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Reconnect retries intentionally handle arbitrary ResoniteLink failures before replacing the client.")]
    private async Task<TResult> ExecuteWithReconnectAsync<TState, TResult>(
        Func<IResoniteLinkClient, TState, CancellationToken, Task<TResult>> operation,
        TState state,
        string operationName,
        CancellationToken cancellationToken,
        Func<TState, string?>? occupancyDescriptorProvider = null,
        bool retryAfterFatalProtocolFailure = false)
    {
        Exception? lastException = null;
        string? occupancyDescriptor = occupancyDescriptorProvider?.Invoke(state);

        for (int attempt = 1; attempt <= AttemptLimit; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ClientLease lease = AcquireCurrentClient(out ClientState observedClient);

            try
            {
                return await operation(lease.Client, state, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (ShouldRetireClientAfterFailure(exception))
                {
                    await ReplaceClientAfterFatalFailureAsync(observedClient, operationName, cancellationToken);

                    if (retryAfterFatalProtocolFailure && attempt < AttemptLimit)
                    {
                        logger.WriteWarning(
                            "ResoniteLink {OperationName} failed on attempt {Attempt}/{AttemptLimit}. Prepared a fresh client after a fatal protocol failure and will retry. Reason: {Reason}",
                            operationName,
                            attempt,
                            AttemptLimit,
                            exception.Message);
                        continue;
                    }

                    logger.WriteWarning(
                        "ResoniteLink {OperationName} retired the active client after a fatal protocol failure. Reason: {Reason}",
                        operationName,
                        exception.Message);
                    throw ResoniteLinkOperationException.Wrap(operationName, exception);
                }

                if (attempt >= AttemptLimit)
                {
                    break;
                }

                logger.WriteWarning(
                    "ResoniteLink {OperationName} failed on attempt {Attempt}/{AttemptLimit}. Reconnecting before retry. Reason: {Reason}",
                    operationName,
                    attempt,
                    AttemptLimit,
                    exception.Message);
                await ReconnectAsync(observedClient, cancellationToken);
            }
        }

        throw ResoniteLinkOperationException.Wrap(operationName, lastException!);
    }

    private async Task ReplaceClientAfterFatalFailureAsync(
        ClientState observedClient,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReconnectAsync(observedClient, cancellationToken);
        }
        catch (Exception reconnectException) when (reconnectException is not OperationCanceledException)
        {
            logger.WriteWarning(
                "ResoniteLink {OperationName} could not prepare a connected replacement client after a fatal protocol failure. Reason: {Reason}",
                operationName,
                reconnectException.Message);
        }
    }

    private ClientLease AcquireCurrentClient(out ClientState observedClient)
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            observedClient = Volatile.Read(ref currentClient);
            if (observedClient.TryAcquire())
            {
                return new ClientLease(observedClient);
            }
        }
    }

    private async Task ReconnectAsync(ClientState observedClient, CancellationToken cancellationToken)
    {
        await reconnectGate.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(observedClient, Volatile.Read(ref currentClient)))
            {
                return;
            }

            if (endpoint is null)
            {
                throw new InvalidOperationException("Cannot reconnect before an endpoint has been established.");
            }

            ClientState replacementClient = new(clientFactory());
            try
            {
                await replacementClient.Client.ConnectAsync(endpoint, cancellationToken);
                knownClients.Add(replacementClient);

                if (!ReferenceEquals(
                    Interlocked.CompareExchange(ref currentClient, replacementClient, observedClient),
                    observedClient))
                {
                    replacementClient.MarkRetired();
                    return;
                }

                observedClient.MarkRetired();
                logger.WriteWarning(
                    "Reconnected ResoniteLink client to {Endpoint}.",
                    endpoint);
            }
            catch
            {
                replacementClient.MarkRetired();
                throw;
            }
        }
        finally
        {
            reconnectGate.Release();
        }
    }

    private void ReplaceClientWithoutConnecting(ClientState observedClient)
    {
        ClientState replacementClient = new(clientFactory());
        knownClients.Add(replacementClient);

        if (!ReferenceEquals(
            Interlocked.CompareExchange(ref currentClient, replacementClient, observedClient),
            observedClient))
        {
            replacementClient.MarkRetired();
            return;
        }

        observedClient.MarkRetired();
    }

    private static bool ShouldRetireClientAfterFailure(Exception exception)
    {
        Exception current = exception;
        while (true)
        {
            if (current is InvalidOperationException invalidOperationException
                && IsFatalProtocolFailureMessage(invalidOperationException.Message))
            {
                return true;
            }

            if (current.InnerException is null)
            {
                return false;
            }

            current = current.InnerException;
        }
    }

    private static bool IsFatalProtocolFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("returned a null response", StringComparison.Ordinal)
            || message.Contains("Previous message was binary payload", StringComparison.Ordinal)
            || message.Contains("binary payload", StringComparison.Ordinal);
    }

    private sealed class ClientState(IResoniteLinkClient client)
    {
        private int activeOperations;
        private int retired;
        private int disposed;
        private int clientDisposed;

        public IResoniteLinkClient Client { get; } = client;

        public bool TryAcquire()
        {
            while (true)
            {
                if (Volatile.Read(ref disposed) != 0)
                {
                    return false;
                }

                Interlocked.Increment(ref activeOperations);
                if (Volatile.Read(ref disposed) == 0)
                {
                    return true;
                }

                Release();
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref activeOperations) == 0)
            {
                TryDisposeIfIdle();
            }
        }

        public void MarkRetired()
        {
            Interlocked.Exchange(ref disposed, 1);
            Interlocked.Exchange(ref retired, 1);
            TryDisposeIfIdle();
        }

        private void TryDisposeIfIdle()
        {
            if (Volatile.Read(ref disposed) == 0
                || Volatile.Read(ref retired) == 0
                || Volatile.Read(ref activeOperations) != 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref clientDisposed, 1) == 0)
            {
                Client.Dispose();
            }
        }
    }

    private readonly struct ClientLease(ClientState client)
        : IDisposable
    {
        public IResoniteLinkClient Client => client.Client;

        public void Dispose()
        {
            client.Release();
        }
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
