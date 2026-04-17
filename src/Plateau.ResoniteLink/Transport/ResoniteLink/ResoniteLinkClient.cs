using System.Text.Json.Serialization;

using ResoniteLink;

namespace Plateau.ResoniteLink.Transport.ResoniteLink;

internal interface IResoniteLinkClient : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken);

    Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken);

    Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken);

    Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken);

    Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken);
}

internal sealed class ResoniteLinkClient : IResoniteLinkClient
{
    static ResoniteLinkClient()
    {
        LinkInterface.SerializationOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }

    private readonly IResoniteLinkTransport link;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private int disposed;

    public ResoniteLinkClient()
        : this(new LinkInterfaceResoniteLinkTransport(new LinkInterface()))
    {
    }

    internal ResoniteLinkClient(
        IResoniteLinkTransport link)
    {
        this.link = link ?? throw new ArgumentNullException(nameof(link));
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

    public async Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        SlotData response = await ExecuteSerializedAsync(
            _ => link.GetSlotDataAsync(
                new GetSlot
                {
                    SlotID = slotId,
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
            _ => link.ImportMeshAsync(request),
            cancellationToken);
        EnsureSuccess(result, "import mesh");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null mesh asset URL.");
    }

    public async Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(textureImport);
        AssetData result = textureImport switch
        {
            ResoniteRawTextureImport rawImport => await ImportRawTextureAsync(rawImport, cancellationToken),
            ResoniteRawHdrTextureImport rawHdrImport => await ImportRawHdrTextureAsync(rawHdrImport, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'."),
        };

        EnsureSuccess(result, "import texture");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null texture asset URL.");
    }

    public async Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        Response response = await ExecuteSerializedAsync(
            _ => link.UpdateComponentAsync(request),
            cancellationToken);
        EnsureSuccess(response, "update component");
    }

    public async Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
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
            _ => link.AddComponentAsync(request),
            cancellationToken);
        EnsureSuccess(response, operationName);
        return ValidateCreatedEntityId(response.EntityId, "component");
    }

    public async Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
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
            _ => link.AddSlotAsync(request),
            cancellationToken);
        EnsureSuccess(response, operationName);
        return ValidateCreatedEntityId(response.EntityId, "slot");
    }

    public async Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        cancellationToken.ThrowIfCancellationRequested();
        ComponentData response = await ExecuteSerializedAsync(
            _ => link.GetComponentDataAsync(
                new GetComponent
                {
                    ComponentID = componentId,
                }),
            cancellationToken);

        return GetOptionalReadData(
            response.Success,
            response.ErrorInfo,
            response.Data,
            "component");
    }

    private Task<AssetData> ImportRawTextureAsync(ResoniteRawTextureImport rawImport, CancellationToken cancellationToken)
    {
        return ExecuteSerializedAsync(
            _ => link.ImportTextureRawAsync(
                new ImportTexture2DRawData
                {
                    Width = rawImport.Width,
                    Height = rawImport.Height,
                    ColorProfile = rawImport.ColorProfile,
                    RawBinaryPayload = rawImport.RawRgba32Bytes,
                }),
            cancellationToken);
    }

    private Task<AssetData> ImportRawHdrTextureAsync(ResoniteRawHdrTextureImport rawHdrImport, CancellationToken cancellationToken)
    {
        return ExecuteSerializedAsync(
            _ => link.ImportTextureRawHdrAsync(
                new ImportTexture2DRawDataHDR
                {
                    Width = rawHdrImport.Width,
                    Height = rawHdrImport.Height,
                    RawBinaryPayload = rawHdrImport.RawRgbaFloatBytes,
                }),
            cancellationToken);
    }

    private async Task<TResult> ExecuteSerializedAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task ExecuteSerializedAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            operationGate.Release();
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
