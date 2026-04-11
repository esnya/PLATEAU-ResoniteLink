using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkSceneBuilderConfigurationTests
{
    [Fact]
    public async Task ConstructorEnablesMeshBakeByDefault()
    {
        await using ResoniteLinkSceneBuilder builder = new(new Uri("ws://localhost:12345/"), progressReporter: null);

        Assert.True(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task ConstructorCanDisableMeshBake()
    {
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            static () => throw new InvalidOperationException("unused"),
            terrainTextureAssetGenerator: null,
            enableMeshBake: false,
            progressReporter: null);

        Assert.False(builder.MeshBakeEnabled);
    }
}
