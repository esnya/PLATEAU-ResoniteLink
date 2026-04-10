using System.Text.Json.Serialization;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteLinkTransport : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task<NewEntityId> AddComponentAsync(AddComponent request, CancellationToken cancellationToken);

    Task<NewEntityId> AddSlotAsync(AddSlot request, CancellationToken cancellationToken);

    Task<BatchResponse> RunDataModelOperationBatchAsync(IReadOnlyList<DataModelOperation> operations, CancellationToken cancellationToken);

    Task<ComponentData> GetComponentDataAsync(GetComponent request, CancellationToken cancellationToken);

    Task<SlotData> GetSlotDataAsync(GetSlot request, CancellationToken cancellationToken);

    Task<AssetData> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken);

    Task<AssetData> ImportTextureAsync(ImportTexture2DFile request, CancellationToken cancellationToken);

    Task<AssetData> ImportTextureAsync(ImportTexture2DRawData request, CancellationToken cancellationToken);

    Task<AssetData> ImportTextureAsync(ImportTexture2DRawDataHDR request, CancellationToken cancellationToken);

    Task<Response> UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken);
}

internal sealed class ResoniteLinkTransport : IResoniteLinkTransport
{
    static ResoniteLinkTransport()
    {
        LinkInterface.SerializationOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }

    private readonly LinkInterface link = new();

    public void Dispose()
    {
        link.Dispose();
    }

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        return link.Connect(endpoint, cancellationToken);
    }

    public Task<NewEntityId> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        return link.AddComponent(request).WaitAsync(cancellationToken);
    }

    public Task<NewEntityId> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        return link.AddSlot(request).WaitAsync(cancellationToken);
    }

    public Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        return link.RunDataModelOperationBatch(operations.ToList()).WaitAsync(cancellationToken);
    }

    public Task<ComponentData> GetComponentDataAsync(GetComponent request, CancellationToken cancellationToken)
    {
        return link.GetComponentData(request).WaitAsync(cancellationToken);
    }

    public Task<SlotData> GetSlotDataAsync(GetSlot request, CancellationToken cancellationToken)
    {
        return link.GetSlotData(request).WaitAsync(cancellationToken);
    }

    public Task<AssetData> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        return link.ImportMesh(request).WaitAsync(cancellationToken);
    }

    public Task<AssetData> ImportTextureAsync(ImportTexture2DFile request, CancellationToken cancellationToken)
    {
        return link.ImportTexture(request).WaitAsync(cancellationToken);
    }

    public Task<AssetData> ImportTextureAsync(ImportTexture2DRawData request, CancellationToken cancellationToken)
    {
        return link.ImportTexture(request).WaitAsync(cancellationToken);
    }

    public Task<AssetData> ImportTextureAsync(ImportTexture2DRawDataHDR request, CancellationToken cancellationToken)
    {
        return link.ImportTexture(request).WaitAsync(cancellationToken);
    }

    public Task<Response> UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        return link.UpdateComponent(request).WaitAsync(cancellationToken);
    }
}
