namespace PlateauResoniteLink.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class StableOpaqueIdTests
{
    [Fact]
    public void Create_IsDeterministicForTheSameOrderedInputs()
    {
        string first = StableOpaqueId.Create(
            "sample",
            builder =>
            {
                builder.Add("alpha");
                builder.Add(42);
                builder.Add(true);
            });
        string second = StableOpaqueId.Create(
            "sample",
            builder =>
            {
                builder.Add("alpha");
                builder.Add(42);
                builder.Add(true);
            });

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_DistinguishesNullEmptyAndInputOrder()
    {
        string nullValue = StableOpaqueId.Create("sample", builder => builder.Add((string?)null));
        string emptyValue = StableOpaqueId.Create("sample", builder => builder.Add(string.Empty));
        string ordered = StableOpaqueId.Create(
            "sample",
            builder =>
            {
                builder.Add("alpha");
                builder.Add(42);
            });
        string reversed = StableOpaqueId.Create(
            "sample",
            builder =>
            {
                builder.Add(42);
                builder.Add("alpha");
            });

        Assert.NotEqual(nullValue, emptyValue);
        Assert.NotEqual(ordered, reversed);
    }

    [Fact]
    public void Create_DistinguishesValueKindsAndHonorsRequestedLength()
    {
        string integerValue = StableOpaqueId.Create("sample", builder => builder.Add(1), hexLength: 8);
        string enumValue = StableOpaqueId.Create("sample", builder => builder.AddEnum(SampleEnum.One), hexLength: 8);
        string nullableMissing = StableOpaqueId.Create("sample", builder => builder.Add((int?)null), hexLength: 8);
        string nullablePresent = StableOpaqueId.Create("sample", builder => builder.Add((int?)1), hexLength: 8);

        Assert.NotEqual(integerValue, enumValue);
        Assert.NotEqual(nullableMissing, nullablePresent);
        Assert.Equal("sample".Length + 1 + 8, integerValue.Length);
    }

    [Fact]
    public void Create_CanCanonicalizeRoundedDoubleInputs()
    {
        string first = StableOpaqueId.Create("sample", builder => builder.AddRounded(1.2345674));
        string second = StableOpaqueId.Create("sample", builder => builder.AddRounded(1.23456749));
        string different = StableOpaqueId.Create("sample", builder => builder.AddRounded(1.2345686));

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void Create_CollapsesRoundedSignedZero()
    {
        string negative = StableOpaqueId.Create("sample", builder => builder.AddRounded(-0.0000004));
        string positive = StableOpaqueId.Create("sample", builder => builder.AddRounded(0.0000004));

        Assert.Equal(negative, positive);
    }

    private enum SampleEnum
    {
        One = 1,
    }
}
