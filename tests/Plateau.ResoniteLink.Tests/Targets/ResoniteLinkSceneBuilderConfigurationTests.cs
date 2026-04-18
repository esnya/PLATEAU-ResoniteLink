
namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class ResoniteLinkSceneBuilderConfigurationTests
{
    [Fact]
    public async Task ConstructorEnablesMeshBakeByDefault()
    {
        await using ResoniteLinkSceneBuilder builder = CreateBuilder();

        Assert.True(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task ConstructorCanDisableMeshBake()
    {
        await using ResoniteLinkSceneBuilder builder = CreateBuilder(enableMeshBake: false);

        Assert.False(builder.MeshBakeEnabled);
    }

    private static ResoniteLinkSceneBuilder CreateBuilder(bool enableMeshBake = true)
    {
        return new ResoniteLinkSceneBuilder(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLinkSceneBuilderDependencies(
                new DelegatingClientSession(),
                new TerrainTextureAssetGenerator()),
            enableMeshBake,
            progressReporter: null);
    }
}
