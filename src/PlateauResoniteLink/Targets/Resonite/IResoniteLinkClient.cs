using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

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

    Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken);

    Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken);
}
