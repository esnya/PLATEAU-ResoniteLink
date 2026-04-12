using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;

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
    private readonly object clientStateGate = new();
    private readonly HashSet<CancellationTokenSource> activeImportCancellations = [];
    private IResoniteLinkClient inner = clientFactory();
    private Uri? endpoint;
    private int generation;
    private int activeImportCount;
    private bool reconnectPending;
    private TaskCompletionSource<bool>? reconnectCompletion;
    private TaskCompletionSource<bool>? importDrainCompletion;

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
        return ExecuteTimedImportAsync(
            (client, state, ct) => ExecuteImportMeshWithTimeoutAsync(
                client,
                state,
                importMeshTimeoutMilliseconds,
                reporter,
                ct),
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
            throw;
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
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task reconnectTask;
                IResoniteLinkClient? client = null;
                CancellationTokenSource? importCancellation = null;
                lock (clientStateGate)
                {
                    if (!reconnectPending)
                    {
                        activeImportCount++;
                        client = inner;
                        importCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        activeImportCancellations.Add(importCancellation);
                        reconnectTask = Task.CompletedTask;
                    }
                    else
                    {
                        reconnectTask = reconnectCompletion?.Task ?? Task.CompletedTask;
                    }
                }

                if (client is null)
                {
                    await reconnectTask.WaitAsync(cancellationToken);
                    continue;
                }

                try
                {
                    return await operation(client, state, importCancellation!.Token);
                }
                finally
                {
                    ReleaseActiveImport(importCancellation!);
                }
            }
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
            throw;
        }
        finally
        {
            operationGate.Release();
        }
    }

    private void ReleaseActiveImport(CancellationTokenSource importCancellation)
    {
        TaskCompletionSource<bool>? importDrainSignal = null;
        lock (clientStateGate)
        {
            activeImportCount--;
            activeImportCancellations.Remove(importCancellation);
            if (activeImportCount == 0 && reconnectPending)
            {
                importDrainSignal = importDrainCompletion;
                importDrainCompletion = null;
            }
        }

        importCancellation.Dispose();
        importDrainSignal?.TrySetResult(true);
    }

    private static async Task<Uri> ExecuteImportMeshWithTimeoutAsync(
        IResoniteLinkClient client,
        ImportMeshRawData request,
        int timeoutMilliseconds,
        Action<string>? reporter,
        CancellationToken cancellationToken)
    {
        if (timeoutMilliseconds <= 0)
        {
            return await client.ImportMeshAsync(request, cancellationToken);
        }

        using CancellationTokenSource timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<Uri> importTask = client.ImportMeshAsync(request, timeoutCancellation.Token);
        if (importTask.IsCompleted)
        {
            return await importTask;
        }

        Task completedTask = await Task.WhenAny(
            importTask,
            Task.Delay(timeoutMilliseconds, cancellationToken));
        if (completedTask == importTask)
        {
            return await importTask;
        }

        await timeoutCancellation.CancelAsync();
        _ = importTask.ContinueWith(
            static completedImportTask => _ = completedImportTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        cancellationToken.ThrowIfCancellationRequested();
        string diagnostic = DescribeClientStateForDiagnostics(client);
        reporter?.Invoke(PlateauLog.Warning("live", diagnostic));
        throw new TimeoutException(
            string.IsNullOrWhiteSpace(diagnostic)
                ? $"ResoniteLink ImportMesh did not complete within {timeoutMilliseconds}ms."
                : $"ResoniteLink ImportMesh did not complete within {timeoutMilliseconds}ms. {diagnostic}");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Diagnostic reflection should never mask the original timeout path.")]
    private static string DescribeClientStateForDiagnostics(IResoniteLinkClient client)
    {
        try
        {
            List<string> clientChain = [];
            object current = client;
            object? link = null;

            for (int depth = 0; depth < 8 && current is not null; depth++)
            {
                clientChain.Add(current.GetType().FullName ?? current.GetType().Name);
                object? nestedLink = GetMemberValue(current, "link");
                if (nestedLink is not null)
                {
                    link = nestedLink;
                    break;
                }

                object? nestedInner = GetMemberValue(current, "inner");
                if (nestedInner is null || ReferenceEquals(nestedInner, current))
                {
                    break;
                }

                current = nestedInner;
            }

            if (link is null)
            {
                return $"[live][diagnostic] ImportMesh timeout client_chain={string.Join(" -> ", clientChain)} link=unavailable.";
            }

            object? websocket = GetMemberValue(link, "_client");
            object? pendingResponses = GetMemberValue(link, "_pendingResponses");
            object? failureException = GetMemberValue(link, "FailureException");
            object? isConnected = GetMemberValue(link, "IsConnected");
            object? websocketState = websocket is null ? null : GetMemberValue(websocket, "State");

            return $"[live][diagnostic] ImportMesh timeout client_chain={string.Join(" -> ", clientChain)} "
                + $"link_type={link.GetType().FullName ?? link.GetType().Name} "
                + $"is_connected={isConnected ?? "unknown"} websocket_state={websocketState ?? "unknown"} "
                + $"pending_responses={TryGetCount(pendingResponses)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} "
                + $"failure_exception={(failureException as Exception)?.GetType().Name ?? failureException?.ToString() ?? "null"}.";
        }
        catch (Exception exception)
        {
            return $"[live][diagnostic] ImportMesh timeout diagnostics failed: {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static object? GetMemberValue(object instance, string memberName)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = instance.GetType();
        FieldInfo? field = type.GetField(memberName, Flags);
        if (field is not null)
        {
            return field.GetValue(instance);
        }

        PropertyInfo? property = type.GetProperty(memberName, Flags);
        return property?.GetValue(instance);
    }

    private static int? TryGetCount(object? instance)
    {
        if (instance is null)
        {
            return null;
        }

        object? count = GetMemberValue(instance, "Count");
        return count switch
        {
            int intCount => intCount,
            long longCount when longCount is <= int.MaxValue and >= int.MinValue => (int)longCount,
            _ => null,
        };
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

            Task waitForImportsTask;
            CancellationTokenSource[] activeImportCancellationsSnapshot;
            TaskCompletionSource<bool> reconnectCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (clientStateGate)
            {
                reconnectPending = true;
                reconnectCompletion = reconnectCompletionSource;
                activeImportCancellationsSnapshot = activeImportCancellations.ToArray();
                waitForImportsTask = activeImportCount == 0
                    ? Task.CompletedTask
                    : (importDrainCompletion ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            if (activeImportCancellationsSnapshot.Length > 0)
            {
                reporter?.Invoke(
                    PlateauLog.Warning(
                        "live",
                        $"Reconnect canceling {activeImportCancellationsSnapshot.Length} active import(s) before retry."));
                await Task.WhenAll(activeImportCancellationsSnapshot.Select(static cancellation => cancellation.CancelAsync()));
            }

            await waitForImportsTask.WaitAsync(cancellationToken);
            IResoniteLinkClient previous = inner;
            IResoniteLinkClient replacement = clientFactory();

            try
            {
                await replacement.ConnectAsync(endpoint, cancellationToken);
            }
            catch
            {
                replacement.Dispose();
                lock (clientStateGate)
                {
                    reconnectPending = false;
                    reconnectCompletion = null;
                }
                reconnectCompletionSource.TrySetResult(true);
                throw;
            }

            lock (clientStateGate)
            {
                inner = replacement;
                reconnectPending = false;
                reconnectCompletion = null;
                importDrainCompletion = null;
                Interlocked.Increment(ref generation);
            }
            reconnectCompletionSource.TrySetResult(true);
            previous.Dispose();
            reporter?.Invoke(PlateauLog.Warning("live", $"Reconnected ResoniteLink client to {endpoint}."));
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
