using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application.Importing;

public sealed class CountingImportedObjectUnitStreamTests
{
    [Fact]
    public async Task ReadAllAsyncReportsCityObjectCountsAsUnitsAreEnumerated()
    {
        List<int> observedCounts = [];
        CountingImportedObjectUnitStream stream = new(
            CreateObjectUnitsAsync(
                CreateObjectUnit("first.gml", "first-a", "first-b"),
                CreateObjectUnit("second.gml", "second-a")),
            observedCounts.Add);

        List<string> observedSources = [];
        await foreach (ImportedObjectUnit objectUnit in stream.ReadAllAsync())
        {
            observedSources.Add(objectUnit.Descriptor.SourceFileRelativePath);
        }

        Assert.Equal([2, 1], observedCounts);
        Assert.Equal(["first.gml", "second.gml"], observedSources);
    }

    [Fact]
    public async Task ReadAllAsyncDoesNotEnumerateSourceBeforeConsumerReads()
    {
        int yieldedUnitCount = 0;
        CountingImportedObjectUnitStream stream = new(
            CreateRecordingObjectUnitsAsync(() => yieldedUnitCount++),
            _ => { });

        IAsyncEnumerable<ImportedObjectUnit> countedUnits = stream.ReadAllAsync();

        Assert.Equal(0, yieldedUnitCount);
        await foreach (ImportedObjectUnit _ in countedUnits)
        {
        }

        Assert.Equal(1, yieldedUnitCount);
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> CreateObjectUnitsAsync(
        params ImportedObjectUnit[] objectUnits)
    {
        await Task.Yield();
        foreach (ImportedObjectUnit objectUnit in objectUnits)
        {
            yield return objectUnit;
        }
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> CreateRecordingObjectUnitsAsync(
        Action onYield)
    {
        await Task.Yield();
        onYield();
        yield return CreateObjectUnit("source.gml", "city-object");
    }

    private static ImportedObjectUnit CreateObjectUnit(string sourceFileRelativePath, params string[] objectKeys)
    {
        return new ImportedObjectUnit(
            sourceFileRelativePath,
            "bldg",
            null,
            objectKeys.Select(CreateCityObject).ToArray());
    }

    private static ImportedCityObject CreateCityObject(string objectKey)
    {
        return new ImportedCityObject(
            ObjectKey: objectKey,
            DisplayName: objectKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Geometry: new TriangleMeshGeometry(new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 0.0, 1.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                ],
                [new MeshSubmesh(0, [0, 1, 2])])),
            Materials: [],
            SourceFileRelativePath: "source.gml");
    }
}
