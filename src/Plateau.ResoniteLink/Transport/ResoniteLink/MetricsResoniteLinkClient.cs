using ResoniteLink;

namespace Plateau.ResoniteLink.Transport.ResoniteLink;

internal sealed class MetricsResoniteLinkClient(
    IResoniteLinkClient inner,
    ResoniteLinkSendDiagnostics diagnostics) : IResoniteLinkClient
{
    public void Dispose()
    {
        inner.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("connect");
        await inner.ConnectAsync(endpoint, cancellationToken);
    }

    public async Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("add_component");
        return await inner.AddComponentAsync(request, cancellationToken);
    }

    public async Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("add_slot");
        return await inner.AddSlotAsync(request, cancellationToken);
    }

    public async Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("batch");
        return await inner.RunDataModelOperationBatchAsync(operations, cancellationToken);
    }

    public async Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("get_slot");
        return await inner.GetSlotAsync(slotId, depth, cancellationToken);
    }

    public async Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("get_component");
        return await inner.GetComponentAsync(componentId, cancellationToken);
    }

    public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("import_mesh");
        return await inner.ImportMeshAsync(request, cancellationToken);
    }

    public async Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("import_texture");
        return await inner.ImportTextureAsync(textureImport, cancellationToken);
    }

    public async Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("update_component");
        await inner.UpdateComponentAsync(request, cancellationToken);
    }
}
