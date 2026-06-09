using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResonitePlacementPolicyTests
{
    private static readonly string[] DuplicateStemPaths = ["udx/bldg/a/sample.gml", "udx/dem/b/sample.gml"];
    private static readonly string[] SourceFilePrefixPackages =
    [
        "area",
        "bldg",
        "brid",
        "cons",
        "dem",
        "fld",
        "frn",
        "gen",
        "htd",
        "ifld",
        "lsld",
        "luse",
        "rfld",
        "rwy",
        "squr",
        "tnm",
        "tran",
        "trk",
        "tun",
        "ubld",
        "unf",
        "urf",
        "veg",
        "wtr",
        "wwy",
    ];

    [Fact]
    public void ResolveRequiredSourceFileRootMeshCode_PrefersConcreteMeshCodeFromSourceFileSlot()
    {
        string resolved = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveRequiredSourceFileRootMeshCode(
            "plateau_tokyo23ku_bldg_53394525",
            "533945");

        Assert.Equal("53394525", resolved);
    }

    [Fact]
    public void ResolveRequiredSourceFileRootMeshCode_PrefersExplicitDescriptorRoot()
    {
        string resolved = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveRequiredSourceFileRootMeshCode(
            "53394526",
            "plateau_tokyo23ku_bldg_53394525",
            "533945");

        Assert.Equal("53394526", resolved);
    }

    [Fact]
    public void ResolveCityObjectLocalPosition_UsesRequestRelativeHorizontalOffsetAndObservedVerticalOffset()
    {
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 originalPosition = new(25.0, 15.0, -10.0);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 observedRootPosition = new(999.0, 5.0, 888.0);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 requestRelativeRootPosition = new(
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin).X,
            observedRootPosition.Y,
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin).Z);

        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 expected = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.Subtract(
            originalPosition,
            requestRelativeRootPosition);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 actual = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveCityObjectLocalPosition(
            requestOrigin,
            "53394525",
            observedRootPosition,
            originalPosition);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EvaluateRootPlacementCorrection_SplitsPlacementAndPostPlacementLayers()
    {
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");

        PlateauResoniteLink.Targets.Resonite.ResonitePlacementCorrectionResult correction =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.EvaluateRootPlacementCorrection(
                requestOrigin,
                "53394525",
                observedRootHeight: 5.0);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 expectedOffset =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin);

        Assert.Empty(correction.Layers.SourceFile);
        Assert.Empty(correction.Layers.Import);
        Assert.Collection(
            correction.Layers.Placement,
            term =>
            {
                Assert.Equal(PlateauResoniteLink.Targets.Resonite.ResoniteCorrectionAxis.X, term.Axis);
                Assert.Equal(expectedOffset.X, term.Value, 6);
                Assert.Equal(
                    PlateauResoniteLink.Targets.Resonite.ResonitePlacementCorrectionReason.RequestRelativeMeshCodeOffset,
                    term.Reason);
            },
            term =>
            {
                Assert.Equal(PlateauResoniteLink.Targets.Resonite.ResoniteCorrectionAxis.Z, term.Axis);
                Assert.Equal(expectedOffset.Z, term.Value, 6);
                Assert.Equal(
                    PlateauResoniteLink.Targets.Resonite.ResonitePlacementCorrectionReason.RequestRelativeMeshCodeOffset,
                    term.Reason);
            });
        Assert.Collection(
            correction.Layers.PostPlacement,
            term =>
            {
                Assert.Equal(PlateauResoniteLink.Targets.Resonite.ResoniteCorrectionAxis.Y, term.Axis);
                Assert.Equal(5.0, term.Value, 6);
                Assert.Equal(
                    PlateauResoniteLink.Targets.Resonite.ResonitePlacementCorrectionReason.ObservedRootHeight,
                    term.Reason);
            });
        Assert.Equal(expectedOffset.X, correction.CorrectedRootPosition.X, 6);
        Assert.Equal(5.0, correction.CorrectedRootPosition.Y, 6);
        Assert.Equal(expectedOffset.Z, correction.CorrectedRootPosition.Z, 6);
    }

    [Fact]
    public void ResolveMeshRootPosition_UsesRequestRelativeHorizontalOffsetAndObservedVerticalOffset()
    {
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 resolved = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(
            requestOrigin,
            "53394525",
            observedRootHeight: 5.0);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 expectedOffset =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin);

        Assert.Equal(expectedOffset.X, resolved.X, 6);
        Assert.Equal(5.0, resolved.Y, 6);
        Assert.Equal(expectedOffset.Z, resolved.Z, 6);
    }

    [Fact]
    public void ResolveParentOriginFromMeshRootPosition_RestoresOriginFromMeshCodeAndHorizontalPosition()
    {
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin parentOrigin = new(35.6875, 139.69375, 0.0);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 firstRootPosition =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(parentOrigin, "53394525");
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 secondRootPosition =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(parentOrigin, "53394526");

        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin firstRecovered =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveParentOriginFromMeshRootPosition("53394525", firstRootPosition);
        PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin secondRecovered =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveParentOriginFromMeshRootPosition("53394526", secondRootPosition);
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 projectedFromFirst =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(firstRecovered, "53394527");
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 projectedFromSecond =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(secondRecovered, "53394527");
        PlateauResoniteLink.Targets.Resonite.ResoniteFloat3 expected =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(parentOrigin, "53394527");

        Assert.Equal(expected.X, projectedFromFirst.X, 3);
        Assert.Equal(expected.Z, projectedFromFirst.Z, 3);
        Assert.Equal(expected.X, projectedFromSecond.X, 3);
        Assert.Equal(expected.Z, projectedFromSecond.Z, 3);
    }

    [Fact]
    public void FormatLodSlotName_UsesLod0ForNullLod()
    {
        string slotName = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.FormatLodSlotName(null);

        Assert.Equal("LOD0", slotName);
    }

    [Fact]
    public void CreateSourceFileSlotNamesByRelativePath_AddsStableHashForDuplicateFileStem()
    {
        IReadOnlyDictionary<string, string> slotNames =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(
                DuplicateStemPaths,
                PlateauResoniteLink.Application.Importing.PlateauSourceFilePackageIndex.CreateByRelativePath(DuplicateStemPaths));

        Assert.Equal(2, slotNames.Count);
        Assert.All(slotNames.Values, static value => Assert.Contains(" sample_", value, StringComparison.Ordinal));
        Assert.NotEqual(slotNames["udx/bldg/a/sample.gml"], slotNames["udx/dem/b/sample.gml"]);
    }

    [Theory]
    [InlineData("area", "<color=hero.purple>🗺️</color> sample")]
    [InlineData("bldg", "<color=hero.cyan>🏢</color> sample")]
    [InlineData("brid", "<color=hero.blue>🌉</color> sample")]
    [InlineData("cons", "<color=hero.orange>🏗️</color> sample")]
    [InlineData("dem", "<color=mid.orange>🟫</color> sample")]
    [InlineData("fld", "<color=mid.cyan>🌊</color> sample")]
    [InlineData("frn", "<color=hero.orange>🚧</color> sample")]
    [InlineData("gen", "<color=hero.purple>📦</color> sample")]
    [InlineData("htd", "<color=hero.red>🔥</color> sample")]
    [InlineData("ifld", "<color=hero.cyan>🌧️</color> sample")]
    [InlineData("lsld", "<color=mid.orange>⛰️</color> sample")]
    [InlineData("luse", "<color=hero.green>🏷️</color> sample")]
    [InlineData("rfld", "<color=hero.blue>🌊</color> sample")]
    [InlineData("rwy", "<color=hero.yellow>🛫</color> sample")]
    [InlineData("squr", "<color=hero.green>🟩</color> sample")]
    [InlineData("tnm", "<color=hero.purple>🗻</color> sample")]
    [InlineData("tran", "<color=hero.yellow>🛣️</color> sample")]
    [InlineData("trk", "<color=hero.orange>🚉</color> sample")]
    [InlineData("tun", "<color=hero.purple>🚇</color> sample")]
    [InlineData("ubld", "<color=mid.cyan>🏬</color> sample")]
    [InlineData("unf", "<color=hero.green>🏞️</color> sample")]
    [InlineData("urf", "<color=hero.purple>🏙️</color> sample")]
    [InlineData("veg", "<color=hero.green>🌿</color> sample")]
    [InlineData("wtr", "<color=mid.cyan>💧</color> sample")]
    [InlineData("wwy", "<color=hero.cyan>⛴️</color> sample")]
    public void CreateSourceFileSlotNamesByRelativePath_PrefixesSupportedPlateauPackageRoots(
        string packageName,
        string expectedSlotName)
    {
        string relativePath = $"udx/{packageName}/53394525/sample.gml";
        IReadOnlyDictionary<string, string> slotNames =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(
                [relativePath],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [relativePath] = packageName,
                });

        Assert.Equal(expectedSlotName, slotNames[relativePath]);
    }

    [Fact]
    public void CreateSourceFileSlotNamesByRelativePath_DoesNotInferUnsupportedPackageFromPath()
    {
        const string relativePath = "udx/trn/53394525/sample.gml";

        IReadOnlyDictionary<string, string> slotNames =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(
                [relativePath],
                PlateauResoniteLink.Application.Importing.PlateauSourceFilePackageIndex.CreateByRelativePath([relativePath]));

        Assert.Equal("sample", slotNames[relativePath]);
    }

    [Fact]
    public void SourceFileRootPrefixPackagesCoverSupportedPlateauPackages()
    {
        Assert.Equal(
            PlateauResoniteLink.Domain.Importing.PlateauPackageCatalog.SupportedPackageNames.Order(StringComparer.Ordinal),
            SourceFilePrefixPackages.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ResolveSourceFileRelativePath_ThrowsWhenSourceFileMetadataIsMissing()
    {
        PlateauResoniteLink.Targets.Resonite.ResoniteConstructionCityObject cityObject = new(
            SlotKey: "slot-a",
            DisplayName: "slot-a",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new PlateauResoniteLink.Targets.Resonite.ResoniteTransform(new PlateauResoniteLink.Targets.Resonite.ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new PlateauResoniteLink.Targets.Resonite.ResoniteImportedMesh([], []),
            Materials: [],
            SourceFileRelativePath: null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveSourceFileRelativePath(cityObject));

        Assert.Contains("SourceFileRelativePath", exception.Message, StringComparison.Ordinal);
    }

    private static PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin RequireMeshCodeCenter(string meshCode)
    {
        if (PlateauResoniteLink.Domain.Importing.PlateauMeshCode.TryGetGeodeticCenter(
            meshCode,
            out PlateauResoniteLink.Domain.Importing.GeodeticCoordinate center))
        {
            return new PlateauResoniteLink.Targets.Resonite.ResoniteLocalOrigin(
                center.Latitude,
                center.Longitude,
                center.Altitude);
        }

        throw new InvalidOperationException($"Failed to resolve a mesh-code center for '{meshCode}'.");
    }
}
