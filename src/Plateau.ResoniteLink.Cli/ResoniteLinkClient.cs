using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteLinkClient : IDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken);

    Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken);

    Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken);

    Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken);

    Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken);

    Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken);

    Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken);
}

internal sealed class ResoniteLinkClient : IResoniteLinkClient
{
    private readonly LinkInterface link = new();

    public void Dispose()
    {
        link.Dispose();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await link.Connect(endpoint, cancellationToken);
    }

    public async Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await link.AddComponent(request);
    }

    public async Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await link.AddSlot(request);
    }

    public async Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ComponentData response = await link.GetComponentData(
            new GetComponent
            {
                ComponentID = componentId,
            });

        return response.Success ? response.Data : null;
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

        return response.Success ? response.Data : null;
    }

    public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssetData result = await link.ImportMesh(request);
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

        return result.AssetURL ?? throw new InvalidOperationException("ResoniteLink returned a null texture asset URL.");
    }

    public async Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await link.UpdateComponent(request);
    }
}
