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

    [Fact]
    public async Task BuildAsyncSendsSameDisplayNameWhenConstructionIdentityDiffers()
    {
        FakeSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateSameDisplayNameDifferentIdentityScene());

        Assert.Equal(2, CountNamedSceneSlots(session, "Repeated Building"));
        Assert.Equal(
            2,
            session.SlotsById.Values.Count(slot =>
                string.Equals(slot.Name?.Value, "Repeated Building", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(slot.Tag?.Value)));
    }

    [Fact]
    public async Task BuildAsyncSkipsSameConstructionIdentityWhenDisplayNameDiffersAcrossAppendRuns()
    {
        FakeSession session = new();

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateIdentityReplayScene(
                displayName: "Original Building",
                slotKey: "bldg_identity_original",
                sourceObjectKey: "building-identity"));

        await RunBuilderAsync(
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => new FakeClient(session)),
            CreateIdentityReplayScene(
                displayName: "Renamed Building",
                slotKey: "bldg_identity_replay",
                sourceObjectKey: "building-identity"));

        Assert.Equal(1, CountNamedSceneSlots(session, "Original Building"));
        Assert.Equal(0, CountNamedSceneSlots(session, "Renamed Building"));
        Assert.DoesNotContain(
            session.SlotsById.Values,
            slot => slot.Name?.Value is not null
                && slot.Name.Value.StartsWith("__identity_", StringComparison.Ordinal));
        Assert.Contains(
            session.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, "Original Building", StringComparison.Ordinal)
                && string.Equals(slot.Tag?.Value, "53394525|bldg|2|building-identity", StringComparison.Ordinal));
    }
}
