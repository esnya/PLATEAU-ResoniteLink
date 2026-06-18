
using System;

using PlateauResoniteLink.Resonite.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemSourceFileBatchingTests
{
    [Fact]
    public void CreateKeyNormalizesPackageNameAndRequiresSourceFileScope()
    {
        ResoniteConstructionCityObject cityObject = CreateCityObject() with
        {
            PackageName = "BLDG",
            SourceFileRelativePath = "udx/bldg/53394525_bldg_6697_op.gml",
        };

        NonDemSourceFileBatchKey key = NonDemSourceFileBatching.CreateKey(
            cityObject,
            NonDemCityObjectBakePolicies.Default);

        Assert.Equal("bldg", key.PackageName);
        Assert.Equal("udx/bldg/53394525_bldg_6697_op.gml", key.SourceFileRelativePath);
        Assert.Throws<InvalidOperationException>(() => NonDemSourceFileBatching.CreateKey(
            cityObject with { SourceFileRelativePath = null },
            NonDemCityObjectBakePolicies.Default));
    }

    [Fact]
    public void CreateBatchNamesUseStableSourceFileAndLodTokens()
    {
        NonDemSourceFileBatchKey key = new(
            ActualMeshCode: "53394525",
            PackageName: "bldg",
            LodLevel: 2,
            PolicyContext: "default",
            SourceFileRelativePath: "udx/bldg/53394525_bldg_6697_op.gml");

        string slotKey = NonDemSourceFileBatching.CreateBatchSlotKey(key, batchIndex: 1);
        string displayName = NonDemSourceFileBatching.CreateBatchDisplayName(key, batchIndex: 1, slotKey);

        Assert.Equal("atlasbake-53394525_bldg_6697_op-bldg-lod2-2", slotKey);
        Assert.Equal("AtlasBake bldg LOD2 #2 [atlasbake-53394525_bldg_6697_op-bldg-lod2-2]", displayName);
    }

    [Fact]
    public void KeyComparerOrdersByMeshPackageLodPolicyAndSourceFile()
    {
        NonDemSourceFileBatchKey first = new("53394525", "bldg", 1, "a", "a.gml");
        NonDemSourceFileBatchKey second = new("53394525", "bldg", 2, "a", "a.gml");

        Assert.True(NonDemSourceFileBatching.KeyComparer.Compare(first, second) < 0);
        Assert.True(NonDemSourceFileBatching.KeyComparer.Compare(second, first) > 0);
        Assert.Equal(0, NonDemSourceFileBatching.KeyComparer.Compare(first, first));
    }

    private static ResoniteConstructionCityObject CreateCityObject()
    {
        return new ResoniteConstructionCityObject(
            "object",
            "object",
            "bldg",
            "53394525",
            LodLevel: 2,
            new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(1.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 1.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 1.0)),
                ],
                [new ResoniteMeshSubmesh(0, [0, 1, 2])]),
            []);
    }
}
