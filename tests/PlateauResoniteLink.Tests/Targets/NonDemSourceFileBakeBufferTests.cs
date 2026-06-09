using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemSourceFileBakeBufferTests
{
    [Fact]
    public void TakeReturnsFoundEntryForExistingSourceFile()
    {
        NonDemSourceFileBakeBuffer buffer = new();
        NonDemSourceFileBatchKey sourceFileKey = CreateSourceFileKey();
        NonDemBufferedCityObject bufferedCityObject = CreateBufferedCityObject();

        buffer.Add(sourceFileKey, bufferedCityObject);

        NonDemSourceFileBakeBufferTakeResult result = buffer.Take(sourceFileKey);

        Assert.True(result.Found);
        Assert.Equal(sourceFileKey, result.Entry.SourceFileKey);
        Assert.Equal(0, result.Entry.BatchStartIndex);
        Assert.Same(bufferedCityObject.CityObject, Assert.Single(result.Entry.CityObjects).CityObject);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void TakeReturnsNotFoundForMissingSourceFile()
    {
        NonDemSourceFileBakeBuffer buffer = new();

        NonDemSourceFileBakeBufferTakeResult result = buffer.Take(CreateSourceFileKey());

        Assert.False(result.Found);
    }

    [Fact]
    public void CompleteAdvancesNextBatchIndexForSourceFile()
    {
        NonDemSourceFileBakeBuffer buffer = new();
        NonDemSourceFileBatchKey sourceFileKey = CreateSourceFileKey();

        buffer.Add(sourceFileKey, CreateBufferedCityObject());
        NonDemSourceFileBakeBufferTakeResult first = buffer.Take(sourceFileKey);
        buffer.Complete(first.Entry, reservedOutputCount: 2);
        buffer.Add(sourceFileKey, CreateBufferedCityObject());

        NonDemSourceFileBakeBufferTakeResult second = buffer.Take(sourceFileKey);

        Assert.True(second.Found);
        Assert.Equal(2, second.Entry.BatchStartIndex);
    }

    private static NonDemSourceFileBatchKey CreateSourceFileKey()
    {
        return new(
            ActualMeshCode: "53394525",
            PackageName: "bldg",
            LodLevel: 2,
            PolicyContext: "non-dem",
            SourceFileRelativePath: "udx/bldg/53394525_bldg_6697_op.gml");
    }

    private static NonDemBufferedCityObject CreateBufferedCityObject()
    {
        return new NonDemBufferedCityObject(
            CreateCityObject(),
            NonDemCityObjectBakePolicies.Default);
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
