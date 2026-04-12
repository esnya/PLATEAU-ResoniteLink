using System.Text.Json.Serialization;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteLinkClient : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken);

    Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken);

    Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken);

    Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken);

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

    public ResoniteLinkClient()
        : this(new LinkInterfaceResoniteLinkTransport(new LinkInterface()))
    {
    }

    internal ResoniteLinkClient(IResoniteLinkTransport link)
    {
        this.link = link ?? throw new ArgumentNullException(nameof(link));
    }

    public void Dispose()
    {
        link.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await link.ConnectAsync(endpoint, cancellationToken);
    }

    public async Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        NewEntityId response = await link.AddComponentAsync(request);
        EnsureSuccess(
            response,
            CreateMutationOperationName(
                "add component",
                request.Data.ComponentType,
                request.ContainerSlotId));
        return ValidateCreatedEntityId(response.EntityId, "component");
    }

    public async Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        NewEntityId response = await link.AddSlotAsync(request);
        EnsureSuccess(
            response,
            CreateMutationOperationName(
                "add slot",
                request.Data.Name?.Value,
                request.Data.Parent?.TargetID));
        return ValidateCreatedEntityId(response.EntityId, "slot");
    }

    public async Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
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

        BatchResponse response = await link.RunDataModelOperationBatchAsync(operations.ToList());
        if (!response.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(response.ErrorInfo)
                    ? "ResoniteLink batch operation failed."
                    : $"ResoniteLink batch operation failed: {response.ErrorInfo}");
        }

        return response;
    }

    public async Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ComponentData response = await link.GetComponentDataAsync(
            new GetComponent
            {
                ComponentID = componentId,
            });

        return GetOptionalReadData(
            response.Success,
            response.ErrorInfo,
            response.Data,
            "component");
    }

    public async Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SlotData response = await link.GetSlotDataAsync(
            new GetSlot
            {
                SlotID = slotId,
                Depth = depth,
                IncludeComponentData = false,
            });

        return GetOptionalReadData(
            response.Success,
            response.ErrorInfo,
            response.Data,
            "slot");
    }

    public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssetData result = await link.ImportMeshAsync(request);
        EnsureSuccess(result, "import mesh");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null mesh asset URL.");
    }

    public async Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(textureImport);
        AssetData result = textureImport switch
        {
            ResoniteFileTextureImport fileImport => await ImportRawTextureFromFileAsync(fileImport, cancellationToken),
            ResoniteRawTextureImport rawImport => await ImportRawTextureAsync(rawImport),
            ResoniteRawHdrTextureImport rawHdrImport => await ImportRawHdrTextureAsync(rawHdrImport),
            _ => throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'."),
        };

        EnsureSuccess(result, "import texture");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null texture asset URL.");
    }

    public async Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Response response = await link.UpdateComponentAsync(request);
        EnsureSuccess(response, "update component");
    }

    private async Task<AssetData> ImportRawTextureFromFileAsync(
        ResoniteFileTextureImport fileImport,
        CancellationToken cancellationToken)
    {
        ResoniteRawTextureImport rawImport = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
            fileImport.AbsolutePath,
            cancellationToken: cancellationToken);
        return await ImportRawTextureAsync(rawImport);
    }

    private Task<AssetData> ImportRawTextureAsync(ResoniteRawTextureImport rawImport)
    {
        return link.ImportTextureRawAsync(
            new ImportTexture2DRawData
            {
                Width = rawImport.Width,
                Height = rawImport.Height,
                ColorProfile = rawImport.ColorProfile,
                RawBinaryPayload = rawImport.RawRgba32Bytes,
            });
    }

    private Task<AssetData> ImportRawHdrTextureAsync(ResoniteRawHdrTextureImport rawHdrImport)
    {
        return link.ImportTextureRawHdrAsync(
            new ImportTexture2DRawDataHDR
            {
                Width = rawHdrImport.Width,
                Height = rawHdrImport.Height,
                RawBinaryPayload = rawHdrImport.RawRgbaFloatBytes,
            });
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

    Task<AssetData> ImportTextureFileAsync(ImportTexture2DFile request);

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

    public Task<AssetData> ImportTextureFileAsync(ImportTexture2DFile request) => inner.ImportTexture(request);

    public Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request) => inner.ImportTexture(request);

    public Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request) => inner.ImportTexture(request);

    public Task<Response> UpdateComponentAsync(UpdateComponent request) => inner.UpdateComponent(request);
}
