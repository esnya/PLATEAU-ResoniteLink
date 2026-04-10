using System.Text.Json.Serialization;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteLinkClient : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken);

    Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken);

    Task RunDataModelOperationBatchAsync(
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

    private readonly LinkInterface link = new();

    public void Dispose()
    {
        link.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await link.Connect(endpoint, cancellationToken);
    }

    public async Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        NewEntityId response = await link.AddComponent(request);
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
        NewEntityId response = await link.AddSlot(request);
        EnsureSuccess(
            response,
            CreateMutationOperationName(
                "add slot",
                request.Data.Name?.Value,
                request.Data.Parent?.TargetID));
        return ValidateCreatedEntityId(response.EntityId, "slot");
    }

    public async Task RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0)
        {
            return;
        }

        BatchResponse response = await link.RunDataModelOperationBatch(operations.ToList());
        if (!response.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(response.ErrorInfo)
                    ? "ResoniteLink batch operation failed."
                    : $"ResoniteLink batch operation failed: {response.ErrorInfo}");
        }
    }

    public async Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ComponentData response = await link.GetComponentData(
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
        SlotData response = await link.GetSlotData(
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
        AssetData result = await link.ImportMesh(request);
        EnsureSuccess(result, "import mesh");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null mesh asset URL.");
    }

    public async Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(textureImport);
        AssetData result = textureImport switch
        {
            ResoniteFileTextureImport fileImport => await link.ImportTexture(
                new ImportTexture2DFile
                {
                    FilePath = fileImport.AbsolutePath,
                }),
            ResoniteRawTextureImport rawImport => await link.ImportTexture(
                new ImportTexture2DRawData
                {
                    Width = rawImport.Width,
                    Height = rawImport.Height,
                    ColorProfile = rawImport.ColorProfile,
                    RawBinaryPayload = rawImport.RawRgba32Bytes,
                }),
            ResoniteRawHdrTextureImport rawHdrImport => await link.ImportTexture(
                new ImportTexture2DRawDataHDR
                {
                    Width = rawHdrImport.Width,
                    Height = rawHdrImport.Height,
                    RawBinaryPayload = rawHdrImport.RawRgbaFloatBytes,
                }),
            _ => throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'."),
        };

        EnsureSuccess(result, "import texture");
        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null texture asset URL.");
    }

    public async Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Response response = await link.UpdateComponent(request);
        EnsureSuccess(response, "update component");
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
