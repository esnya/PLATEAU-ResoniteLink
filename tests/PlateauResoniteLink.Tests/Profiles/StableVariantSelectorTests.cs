using PlateauResoniteLink.Plateau.Application.Importing.Plateau;

using System;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class StableVariantSelectorTests
{
    [Fact]
    public void SelectBucketIsStableForKnownKeys()
    {
        Assert.Equal(2, StableVariantSelector.SelectBucket("53394525:bldg:wall", 6));
        Assert.Equal(2, StableVariantSelector.SelectBucket("53394525:bldg:roof", 4));
        Assert.Equal(0, StableVariantSelector.SelectBucket("53394525:other", 9));
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("   ", 2)]
    public void SelectBucketPreservesEmptyAndWhitespaceKeyHashing(string variantSelectionKey, int expectedBucket)
    {
        Assert.Equal(expectedBucket, StableVariantSelector.SelectBucket(variantSelectionKey, 6));
    }

    [Fact]
    public void SelectBucketRejectsNullKey()
    {
        Assert.Throws<ArgumentNullException>(
            () => StableVariantSelector.SelectBucket(null!, 6));
    }

    [Fact]
    public void SelectBucketRejectsInvalidBucketCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StableVariantSelector.SelectBucket("53394525:bldg:wall", 0));
    }
}
