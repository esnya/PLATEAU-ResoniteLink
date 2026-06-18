
using System;

using PlateauResoniteLink.Resonite.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemCityObjectBakeCandidateFactoryTests
{
    [Fact]
    public void BakeEntryVariantsRejectNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => new NonDemBakeEntry.Atlas(null!));
        Assert.Throws<ArgumentNullException>(() => new NonDemBakeEntry.Preserved(null!));
    }
}
