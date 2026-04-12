using Plateau.ResoniteLink.Cli;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkClientTests
{
    [Fact]
    public void EnsureSuccessThrowsProtocolErrorWhenResponseIsNull()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteLinkClient.EnsureSuccess(null, "add component '[FrooxEngine]FrooxEngine.MeshRenderer' on 'slot-1'"));

        Assert.Equal(
            "ResoniteLink add component '[FrooxEngine]FrooxEngine.MeshRenderer' on 'slot-1' returned a null response.",
            exception.Message);
    }

    [Fact]
    public void EnsureSuccessThrowsErrorInfoWhenResponseFails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteLinkClient.EnsureSuccess(
                new Response
                {
                    Success = false,
                    ErrorInfo = "server said no",
                },
                "add slot 'Assets' on 'Root'"));

        Assert.Equal(
            "ResoniteLink add slot 'Assets' on 'Root' failed: server said no",
            exception.Message);
    }

    [Fact]
    public async Task ImportTextureAsyncFallsBackToRawPayloadWhenFileImportPathIsNotResolvableByListener()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string texturePath = Path.Combine(temporaryDirectory.Path, "albedo.png");
        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 0, 0, 255)))
        {
            await image.SaveAsPngAsync(texturePath);
        }

        using FakeResoniteLinkTransport transport = new()
        {
            ImportTextureFileResult = new AssetData
            {
                Success = false,
                ErrorInfo = $"Exception when generating file signature {texturePath}{Environment.NewLine}System.IO.FileNotFoundException: Could not find file '/home/resonite/{texturePath}'.",
            },
            ImportTextureRawResult = new AssetData
            {
                Success = true,
                AssetURL = new Uri("file:///tmp/albedo.png"),
            },
        };

        using ResoniteLinkClient client = new(transport);
        Uri importedTexture = await client.ImportTextureAsync(
            ResoniteTextureImportFactory.CreateFromFile(texturePath),
            CancellationToken.None);

        Assert.Equal(new Uri("file:///tmp/albedo.png"), importedTexture);
        Assert.Equal(1, transport.ImportTextureFileCallCount);
        Assert.Equal(1, transport.ImportTextureRawCallCount);
        Assert.NotNull(transport.LastRawTextureRequest);
        Assert.Equal(1, transport.LastRawTextureRequest!.Width);
        Assert.Equal(1, transport.LastRawTextureRequest.Height);
    }

    [Fact]
    public async Task ImportTextureAsyncDoesNotFallbackWhenLocalFileIsMissing()
    {
        string texturePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        using FakeResoniteLinkTransport transport = new()
        {
            ImportTextureFileResult = new AssetData
            {
                Success = false,
                ErrorInfo = "Exception when generating file signature missing texture",
            },
        };

        using ResoniteLinkClient client = new(transport);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.ImportTextureAsync(
                ResoniteTextureImportFactory.CreateFromFile(texturePath),
                CancellationToken.None));

        Assert.Contains("ResoniteLink import texture failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, transport.ImportTextureFileCallCount);
        Assert.Equal(0, transport.ImportTextureRawCallCount);
    }

    private sealed class FakeResoniteLinkTransport : IResoniteLinkTransport
    {
        public AssetData ImportTextureFileResult { get; set; } = new() { Success = true };

        public AssetData ImportTextureRawResult { get; set; } = new() { Success = true };

        public int ImportTextureFileCallCount { get; private set; }

        public int ImportTextureRawCallCount { get; private set; }

        public ImportTexture2DRawData? LastRawTextureRequest { get; private set; }

        public void Dispose()
        {
        }

        public Task<NewEntityId> AddComponentAsync(AddComponent request) => throw new NotSupportedException();

        public Task<NewEntityId> AddSlotAsync(AddSlot request) => throw new NotSupportedException();

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ComponentData> GetComponentDataAsync(GetComponent request) => throw new NotSupportedException();

        public Task<SlotData> GetSlotDataAsync(GetSlot request) => throw new NotSupportedException();

        public Task<AssetData> ImportMeshAsync(ImportMeshRawData request) => throw new NotSupportedException();

        public Task<AssetData> ImportTextureFileAsync(ImportTexture2DFile request)
        {
            ImportTextureFileCallCount++;
            return Task.FromResult(ImportTextureFileResult);
        }

        public Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request)
        {
            ImportTextureRawCallCount++;
            LastRawTextureRequest = request;
            return Task.FromResult(ImportTextureRawResult);
        }

        public Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request) => throw new NotSupportedException();

        public Task<BatchResponse> RunDataModelOperationBatchAsync(List<DataModelOperation> operations) => throw new NotSupportedException();

        public Task<Response> UpdateComponentAsync(UpdateComponent request) => throw new NotSupportedException();
    }
}
