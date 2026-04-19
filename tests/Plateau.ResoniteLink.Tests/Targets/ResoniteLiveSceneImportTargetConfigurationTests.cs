
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSceneImportTargetConfigurationTests
{
    [Fact]
    public async Task ConstructorEnablesMeshBakeByDefault()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder();

        Assert.True(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task ConstructorCanDisableMeshBake()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder(enableMeshBake: false);

        Assert.False(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task ConstructorUsesLargeMemoryProfileByDefault()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder();

        Assert.Equal(PlateauImportMemoryProfile.Large, builder.MemoryProfile);
    }

    private static ResoniteLiveSceneImportTarget CreateBuilder(bool enableMeshBake = true)
    {
        return new ResoniteLiveSceneImportTarget(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            PlateauImportMemoryProfile.Large,
            new ResoniteLiveSceneImportDependencies(
                new DelegatingClientSession(),
                new TerrainTextureAssetGenerator()),
            enableMeshBake,
            progressReporter: null);
    }
}
