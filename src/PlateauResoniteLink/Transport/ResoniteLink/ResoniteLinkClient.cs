using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;

using ResoniteLink;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal interface IResoniteLinkClient : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken);

    Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken);

    Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken);

    Task<Component?> GetComponentAsync(ResoniteTransportComponentLocator component, CancellationToken cancellationToken);

    Task<Slot?> GetSlotAsync(ResoniteTransportSlotLocator slot, int depth, CancellationToken cancellationToken);

    Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken);

    Task<Uri> ImportTextureAsync(ITextureImportSource textureSource, CancellationToken cancellationToken);

    Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken);
}

internal sealed class ResoniteLinkClient : IResoniteLinkClient
{
    private static readonly TimeSpan DefaultGateWaitLogThreshold = TimeSpan.FromSeconds(1);

    static ResoniteLinkClient()
    {
        LinkInterface.SerializationOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }

    private readonly IResoniteLinkTransport link;
    private readonly Action<string>? reporter;
    private readonly TimeSpan gateWaitLogThreshold;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private int disposed;

    public ResoniteLinkClient(Action<string>? reporter = null)
        : this(new LinkInterfaceResoniteLinkTransport(new LinkInterface()), reporter)
    {
    }

    internal ResoniteLinkClient(
        IResoniteLinkTransport link,
        Action<string>? reporter = null,
        TimeSpan? gateWaitLogThreshold = null)
    {
        this.link = link ?? throw new ArgumentNullException(nameof(link));
        this.reporter = reporter;
        this.gateWaitLogThreshold = gateWaitLogThreshold ?? DefaultGateWaitLogThreshold;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        operationGate.Dispose();
        link.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await ExecuteSerializedAsync(
            "connect",
            ct => link.ConnectAsync(endpoint, ct),
            cancellationToken);
    }

    public async Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0)
        {
            return new BatchResponse
            {
                Success = true,
                Responses = [],
            };
        }

        BatchResponse response = await ExecuteSerializedAsync(
            "run_data_model_operation_batch",
            _ => link.RunDataModelOperationBatchAsync(operations.ToList()),
            cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(response.ErrorInfo)
                    ? "ResoniteLink batch operation failed."
                    : $"ResoniteLink batch operation failed: {response.ErrorInfo}");
        }

        return response;
    }

    public async Task<Slot?> GetSlotAsync(ResoniteTransportSlotLocator slot, int depth, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        SlotData response = await ExecuteSerializedAsync(
            "get_slot",
            _ => link.GetSlotDataAsync(
                new GetSlot
                {
                    SlotID = slot.Value,
                    Depth = depth,
                    IncludeComponentData = true,
                }),
            cancellationToken);

        return GetOptionalReadData(
            response.Success,
            response.ErrorInfo,
            response.Data,
            "slot");
    }

    public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        AssetData result = await ExecuteSerializedAsync(
            "import_mesh",
            _ => link.ImportMeshAsync(request),
            cancellationToken);
        EnsureSuccess(result, "import mesh");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null mesh asset URL.");
    }

    public async Task<Uri> ImportTextureAsync(ITextureImportSource textureSource, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(textureSource);
        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            textureSource,
            cancellationToken);
        AssetData result = rawPayload.Format switch
        {
            RawTexturePayloadFormat.Rgba32 => await ImportRawTextureAsync(rawPayload, cancellationToken),
            RawTexturePayloadFormat.RgbaFloat32 => await ImportRawHdrTextureAsync(rawPayload, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported texture payload format '{rawPayload.Format}'."),
        };

        EnsureSuccess(result, "import texture");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null texture asset URL.");
    }

    public async Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        Response response = await ExecuteSerializedAsync(
            "update_component",
            _ => link.UpdateComponentAsync(
                new UpdateComponent
                {
                    Data = new Component
                    {
                        ID = request.Component.Value,
                        Members = new Dictionary<string, Member>(request.Members, StringComparer.Ordinal),
                    },
                }),
            cancellationToken);
        EnsureSuccess(response, "update component");
    }

    public async Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        string operationName = CreateMutationOperationName(
            "add component",
            request.Data.ComponentType,
            request.ContainerSlotId);
        NewEntityId response = await ExecuteSerializedAsync(
            "add_component",
            _ => link.AddComponentAsync(request),
            cancellationToken);
        EnsureSuccess(response, operationName);
        return new ResoniteTransportComponentCreationResult(
            new ResoniteTransportComponentLocator(ValidateCreatedEntityId(response.EntityId, "component")));
    }

    public async Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        string operationName = CreateMutationOperationName(
            "add slot",
            request.Data.Name?.Value,
            request.Data.Parent?.TargetID);
        NewEntityId response = await ExecuteSerializedAsync(
            "add_slot",
            _ => link.AddSlotAsync(request),
            cancellationToken);
        EnsureSuccess(response, operationName);
        return new ResoniteTransportSlotCreationResult(
            new ResoniteTransportSlotLocator(ValidateCreatedEntityId(response.EntityId, "slot")));
    }

    public async Task<Component?> GetComponentAsync(ResoniteTransportComponentLocator component, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ComponentData response = await ExecuteSerializedAsync(
            "get_component",
            _ => link.GetComponentDataAsync(
                new GetComponent
                {
                    ComponentID = component.Value,
                }),
            cancellationToken);

        return GetOptionalReadData(
            response.Success,
            response.ErrorInfo,
            response.Data,
            "component");
    }

    private Task<AssetData> ImportRawTextureAsync(RawTexturePayload rawPayload, CancellationToken cancellationToken)
    {
        return ExecuteSerializedAsync(
            "import_texture",
            _ => link.ImportTextureRawAsync(
                new ImportTexture2DRawData
                {
                    Width = rawPayload.Width,
                    Height = rawPayload.Height,
                    ColorProfile = rawPayload.ColorProfile ?? ResoniteTextureColorProfiles.Srgb,
                    RawBinaryPayload = rawPayload.Bytes,
                }),
            cancellationToken);
    }

    private Task<AssetData> ImportRawHdrTextureAsync(RawTexturePayload rawPayload, CancellationToken cancellationToken)
    {
        return ExecuteSerializedAsync(
            "import_texture",
            _ => link.ImportTextureRawHdrAsync(
                new ImportTexture2DRawDataHDR
                {
                    Width = rawPayload.Width,
                    Height = rawPayload.Height,
                    RawBinaryPayload = rawPayload.Bytes,
                }),
            cancellationToken);
    }

    private async Task<TResult> ExecuteSerializedAsync<TResult>(
        string operationName,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        Stopwatch waitStopwatch = Stopwatch.StartNew();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            waitStopwatch.Stop();
            ReportGateWait(operationName, waitStopwatch.Elapsed);

            Stopwatch operationStopwatch = Stopwatch.StartNew();
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                operationStopwatch.Stop();
                ReportRpcCompleted(operationName, operationStopwatch.Elapsed);
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task ExecuteSerializedAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        Stopwatch waitStopwatch = Stopwatch.StartNew();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            waitStopwatch.Stop();
            ReportGateWait(operationName, waitStopwatch.Elapsed);

            Stopwatch operationStopwatch = Stopwatch.StartNew();
            try
            {
                await operation(cancellationToken);
            }
            finally
            {
                operationStopwatch.Stop();
                ReportRpcCompleted(operationName, operationStopwatch.Elapsed);
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private void ReportGateWait(string operationName, TimeSpan elapsed)
    {
        if (reporter is null || elapsed < gateWaitLogThreshold)
        {
            return;
        }

        Report(
            PlateauLog.Debug(
                "live",
                $"ResoniteLink '{operationName}' RPC waited {elapsed.TotalSeconds:F3}s for the per-link request gate."));
    }

    private void ReportRpcCompleted(string operationName, TimeSpan elapsed)
    {
        Report(
            PlateauLog.Debug(
                "live",
                $"ResoniteLink '{operationName}' RPC execution completed in {elapsed.TotalSeconds:F3}s."));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Progress reporting is diagnostic output and must not affect ResoniteLink gate ownership or RPC results.")]
    private void Report(string message)
    {
        try
        {
            reporter?.Invoke(message);
        }
        catch
        {
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    internal static void EnsureSuccess(Response? response, string operationName)
    {
        if (response is null)
        {
            throw new InvalidOperationException($"ResoniteLink {operationName} returned a null response.");
        }

        if (response.Success)
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorInfo)
                ? $"ResoniteLink {operationName} failed."
                : $"ResoniteLink {operationName} failed: {response.ErrorInfo}");
    }

    private static string CreateMutationOperationName(string operationName, string? subject, string? containerId)
    {
        string subjectSuffix = string.IsNullOrWhiteSpace(subject) ? string.Empty : $" '{subject}'";
        string containerSuffix = string.IsNullOrWhiteSpace(containerId) ? string.Empty : $" on '{containerId}'";
        return $"{operationName}{subjectSuffix}{containerSuffix}";
    }

    private static string ValidateCreatedEntityId(string? responseEntityId, string entityKind)
    {
        if (string.IsNullOrWhiteSpace(responseEntityId))
        {
            throw new InvalidOperationException($"ResoniteLink returned a null {entityKind} ID.");
        }

        return responseEntityId;
    }

    private static TData? GetOptionalReadData<TData>(
        bool success,
        string? errorInfo,
        TData? data,
        string entityKind)
        where TData : class
    {
        if (success)
        {
            return data;
        }

        if (string.IsNullOrWhiteSpace(errorInfo))
        {
            return null;
        }

        throw new InvalidOperationException($"ResoniteLink get {entityKind} failed: {errorInfo}");
    }
}

internal interface IResoniteLinkTransport : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task<NewEntityId> AddComponentAsync(AddComponent request);

    Task<NewEntityId> AddSlotAsync(AddSlot request);

    Task<BatchResponse> RunDataModelOperationBatchAsync(List<DataModelOperation> operations);

    Task<ComponentData> GetComponentDataAsync(GetComponent request);

    Task<SlotData> GetSlotDataAsync(GetSlot request);

    Task<AssetData> ImportMeshAsync(ImportMeshRawData request);

    Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request);

    Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request);

    Task<Response> UpdateComponentAsync(UpdateComponent request);
}

internal sealed class LinkInterfaceResoniteLinkTransport(LinkInterface inner) : IResoniteLinkTransport
{
    public void Dispose()
    {
        inner.Dispose();
    }

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => inner.Connect(endpoint, cancellationToken);

    public Task<NewEntityId> AddComponentAsync(AddComponent request) => inner.AddComponent(request);

    public Task<NewEntityId> AddSlotAsync(AddSlot request) => inner.AddSlot(request);

    public Task<BatchResponse> RunDataModelOperationBatchAsync(List<DataModelOperation> operations) =>
        inner.RunDataModelOperationBatch(operations);

    public Task<ComponentData> GetComponentDataAsync(GetComponent request) => inner.GetComponentData(request);

    public Task<SlotData> GetSlotDataAsync(GetSlot request) => inner.GetSlotData(request);

    public Task<AssetData> ImportMeshAsync(ImportMeshRawData request) => inner.ImportMesh(request);

    public Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request) => inner.ImportTexture(request);

    public Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request) => inner.ImportTexture(request);

    public Task<Response> UpdateComponentAsync(UpdateComponent request) => inner.UpdateComponent(request);
}
