using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

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

    public async Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("add_component");
        return await inner.AddComponentAsync(request, cancellationToken);
    }

    public async Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
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

    public async Task<Slot?> GetSlotAsync(ResoniteTransportSlotLocator slot, int depth, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("get_slot");
        return await inner.GetSlotAsync(slot, depth, cancellationToken);
    }

    public async Task<Component?> GetComponentAsync(ResoniteTransportComponentLocator component, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("get_component");
        return await inner.GetComponentAsync(component, cancellationToken);
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

    public async Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
    {
        diagnostics.RecordRpcCall("update_component");
        await inner.UpdateComponentAsync(request, cancellationToken);
    }
}
