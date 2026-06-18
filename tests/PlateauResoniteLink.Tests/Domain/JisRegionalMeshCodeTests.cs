
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Domain;

public sealed class JisRegionalMeshCodeTests
{
    [Fact]
    public void TryParseAcceptsOnlyMatchingMeshOrder()
    {
        Assert.True(FirstRegionalMeshCode.TryParse("5339", out FirstRegionalMeshCode first));
        Assert.True(SecondRegionalMeshCode.TryParse("533945", out SecondRegionalMeshCode second));
        Assert.True(ThirdRegionalMeshCode.TryParse("53394525", out ThirdRegionalMeshCode third));

        Assert.Equal("5339", first.Value);
        Assert.Equal("533945", second.Value);
        Assert.Equal("53394525", third.Value);
        Assert.False(FirstRegionalMeshCode.TryParse("533945", out _));
        Assert.False(SecondRegionalMeshCode.TryParse("53394525", out _));
        Assert.False(ThirdRegionalMeshCode.TryParse("533945", out _));
        Assert.False(ThirdRegionalMeshCode.TryParse(".*", out _));
    }

    [Fact]
    public void BoundsMatchJisRegionalMeshOrder()
    {
        JisRegionalMeshBounds first = FirstRegionalMeshCode.Parse("5339").Bounds;
        JisRegionalMeshBounds second = SecondRegionalMeshCode.Parse("533945").Bounds;
        JisRegionalMeshBounds third = ThirdRegionalMeshCode.Parse("53394525").Bounds;

        Assert.Equal(35.333333333333336, first.SouthLatitude, precision: 12);
        Assert.Equal(139.0, first.WestLongitude, precision: 12);
        Assert.Equal(35.666666666666664, second.SouthLatitude, precision: 12);
        Assert.Equal(139.625, second.WestLongitude, precision: 12);
        Assert.Equal(35.68333333333333, third.SouthLatitude, precision: 12);
        Assert.Equal(139.6875, third.WestLongitude, precision: 12);
    }

    [Fact]
    public void ThirdMeshExposesSecondAndFirstParents()
    {
        ThirdRegionalMeshCode third = ThirdRegionalMeshCode.Parse("53394525");

        Assert.Equal("533945", third.Parent.Value);
        Assert.Equal("5339", third.FirstMesh.Value);
        Assert.Equal("5339", third.Parent.Parent.Value);
    }
}
