using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ImportedObjectUnitTests
{
    [Fact]
    public void ConstructorRejectsEmptyCityObjects()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new ImportedObjectUnit(
                "source.gml",
                "bldg",
                new DetailLevel(1),
                []));

        Assert.Contains("at least one city object", exception.Message, StringComparison.Ordinal);
    }
}
