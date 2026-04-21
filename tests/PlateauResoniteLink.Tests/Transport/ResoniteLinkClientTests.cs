using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Transport;

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
    public async Task ImportTextureAsyncUsesRawPayloadImport()
    {
        using FakeResoniteLinkTransport transport = new()
        {
            ImportTextureRawResult = new AssetData
            {
                Success = true,
                AssetURL = new Uri("file:///tmp/albedo.png"),
            },
        };

        using ResoniteLinkClient client = new(transport);
        Uri importedTexture = await client.ImportTextureAsync(
            new ResoniteRawTextureImport(
                Width: 1,
                Height: 1,
                ColorProfile: ResoniteTextureColorProfiles.Srgb,
                RawRgba32Bytes: [255, 0, 0, 255]),
            CancellationToken.None);

        Assert.Equal(new Uri("file:///tmp/albedo.png"), importedTexture);
        Assert.Equal(1, transport.ImportTextureRawCallCount);
        Assert.NotNull(transport.LastRawTextureRequest);
        Assert.Equal(1, transport.LastRawTextureRequest!.Width);
        Assert.Equal(1, transport.LastRawTextureRequest.Height);
    }

    [Fact]
    public async Task CreateRawFromFileAsyncThrowsWhenLocalFileIsMissing()
    {
        string texturePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await ResoniteTextureImportFactory.CreateRawFromFileAsync(texturePath, cancellationToken: CancellationToken.None));

        Assert.Contains(Path.GetFileName(texturePath), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRawFromFileAsyncLoadsImageWhenRunningUnderWsl()
    {
        using EnvironmentVariableScope scope = new("WSL_DISTRO_NAME", "Ubuntu-24.04");
        using TemporaryDirectory temporaryDirectory = new();
        string texturePath = Path.Combine(temporaryDirectory.Path, "albedo.png");
        using (Image<Rgba32> image = new(1, 1, new Rgba32(255, 0, 0, 255)))
        {
            await image.SaveAsPngAsync(texturePath);
        }

        ResoniteRawTextureImport importedTexture = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
            texturePath,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, importedTexture.Width);
        Assert.Equal(1, importedTexture.Height);
        Assert.Equal(ResoniteTextureColorProfiles.Srgb, importedTexture.ColorProfile);
        Assert.Equal([255, 0, 0, 255], importedTexture.RawRgba32Bytes);
    }

    [Fact]
    public async Task ImportTextureAsyncSerializesOtherOperationsOnSameLink()
    {
        using BlockingResoniteLinkTransport transport = new();
        using IResoniteLinkClient client = new ResoniteLinkClient(transport);

        Task<Uri> importTask = client.ImportTextureAsync(
            new ResoniteRawTextureImport(
                Width: 1,
                Height: 1,
                RawRgba32Bytes: [255, 0, 0, 255],
                ColorProfile: ResoniteTextureColorProfiles.Srgb),
            CancellationToken.None);

        await transport.ImportTextureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<string> addSlotTask = ((IResoniteLinkClient)client).AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference
                    {
                        TargetID = "Root",
                    },
                    Name = new Field_string
                    {
                        Value = "Serialized",
                    },
                },
            },
            CancellationToken.None);

        await Task.Delay(100);
        Assert.False(addSlotTask.IsCompleted);
        Assert.Equal(0, transport.AddSlotCallCount);

        transport.AllowImportTextureCompletion.TrySetResult();

        await importTask;
        string slotId = await addSlotTask;

        Assert.Equal("srv_slot_1", slotId);
        Assert.Equal(1, transport.ImportTextureRawCallCount);
        Assert.Equal(1, transport.AddSlotCallCount);
    }

    private sealed class FakeResoniteLinkTransport : IResoniteLinkTransport
    {
        public AssetData ImportTextureRawResult { get; set; } = new() { Success = true };

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

    private sealed class BlockingResoniteLinkTransport : IResoniteLinkTransport
    {
        private int nextSlotId;

        public int AddSlotCallCount { get; private set; }

        public int ImportTextureRawCallCount { get; private set; }

        public TaskCompletionSource ImportTextureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowImportTextureCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
        }

        public Task<NewEntityId> AddComponentAsync(AddComponent request) => throw new NotSupportedException();

        public Task<NewEntityId> AddSlotAsync(AddSlot request)
        {
            AddSlotCallCount++;
            return Task.FromResult(new NewEntityId
            {
                Success = true,
                EntityId = $"srv_slot_{Interlocked.Increment(ref nextSlotId)}",
            });
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ComponentData> GetComponentDataAsync(GetComponent request) => throw new NotSupportedException();

        public Task<SlotData> GetSlotDataAsync(GetSlot request) => throw new NotSupportedException();

        public Task<AssetData> ImportMeshAsync(ImportMeshRawData request) => throw new NotSupportedException();

        public async Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request)
        {
            ImportTextureRawCallCount++;
            ImportTextureStarted.TrySetResult();
            await AllowImportTextureCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return new AssetData
            {
                Success = true,
                AssetURL = new Uri("resdb:///texture/serialized", UriKind.Absolute),
            };
        }

        public Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request) => throw new NotSupportedException();

        public Task<BatchResponse> RunDataModelOperationBatchAsync(List<DataModelOperation> operations) => throw new NotSupportedException();

        public Task<Response> UpdateComponentAsync(UpdateComponent request) => throw new NotSupportedException();
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string variableName;
        private readonly string? previousValue;

        public EnvironmentVariableScope(string variableName, string? value)
        {
            this.variableName = variableName;
            previousValue = Environment.GetEnvironmentVariable(variableName);
            Environment.SetEnvironmentVariable(variableName, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }
}
