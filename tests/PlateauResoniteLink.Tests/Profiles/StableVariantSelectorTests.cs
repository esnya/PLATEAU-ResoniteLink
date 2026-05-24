using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class StableVariantSelectorTests
{
    [Theory]
    [InlineData("bldg:uv", 4, 2, 0)]
    [InlineData("tran:tri:0", 1, 0, 0)]
    public void SelectBucketUsesStableSha256LittleEndianBuckets(
        string key,
        int expectedBucketOfFive,
        int expectedBucketOfFour,
        int expectedBucketOfTwo)
    {
        Assert.Equal(expectedBucketOfFive, StableVariantSelector.SelectBucket(key, 5));
        Assert.Equal(expectedBucketOfFour, StableVariantSelector.SelectBucket(key, 4));
        Assert.Equal(expectedBucketOfTwo, StableVariantSelector.SelectBucket(key, 2));
    }

    [Fact]
    public void IsWeightedAlternateUsesSaltedBucket()
    {
        Assert.False(StableVariantSelector.IsWeightedAlternate("bldg:uv", "residential-wall-weight", 5));
    }
}
