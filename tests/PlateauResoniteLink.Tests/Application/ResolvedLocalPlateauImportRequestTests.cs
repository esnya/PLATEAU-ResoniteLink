using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ResolvedLocalPlateauImportRequestTests
{
    [Fact]
    public void ConstructorTrimsResolvedBoundaryStrings()
    {
        ResolvedLocalPlateauImportRequest request = new(
            Dataset: " plateau-13213 ",
            MeshCode: " 53395325 ",
            CityGmlLocalSourcePath: " C:/data/source.zip ",
            DemTextureSource: new LocalDatasetLocation(" C:/data/ortho.7z "));

        Assert.Equal("plateau-13213", request.Dataset);
        Assert.Equal("53395325", request.MeshCode);
        Assert.Equal("C:/data/source.zip", request.CityGmlLocalSourcePath);
        Assert.Equal("C:/data/ortho.7z", request.DemTextureLocalSourcePath);
    }
}
