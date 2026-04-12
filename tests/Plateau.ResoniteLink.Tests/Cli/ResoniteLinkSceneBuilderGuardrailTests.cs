using Plateau.ResoniteLink.Cli;

using static Plateau.ResoniteLink.Tests.Cli.ResoniteLinkSceneBuilderGuardrailTestSupport;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkSceneBuilderGuardrailTests
{
    [Fact]
    public async Task BeginAsyncReusesExistingDatasetRootAssetsAndCommonWhenGetSlotOmitsComponents()
    {
        FakeSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateChildRequestChildBuildingScene());

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session, omitComponentDataFromGetSlot: true)),
            CreateChildRequestChildBuildingScene());

        Assert.Equal(1, CountExactPath(session, "PLATEAU tokyo23ku"));
        Assert.Equal(1, CountExactPath(session, "PLATEAU tokyo23ku/Assets"));
        Assert.Equal(1, CountExactPath(session, "PLATEAU tokyo23ku/Assets/Common"));
    }

    [Fact]
    public async Task BuildAsyncSkipsOverlappingParentChildBuildingAppendDuplicates()
    {
        FakeSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateParentRequestChildBuildingScene());

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateChildRequestChildBuildingScene());

        Assert.Equal(1, CountNamedSceneSlots(session, "Building 25"));
    }

    [Fact]
    public async Task BuildAsyncSkipsOverlappingParentChildDemAppendDuplicates()
    {
        FakeSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateParentRequestSharedDemScene());

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateChildRequestSharedDemScene());

        Assert.Equal(1, CountNamedSceneSlots(session, "Shared Terrain"));
    }
}
