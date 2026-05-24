using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteTexturePayloadReferenceComparer : IEqualityComparer<ResoniteTexturePayload>
{
    public static readonly ResoniteTexturePayloadReferenceComparer Instance = new();

    private ResoniteTexturePayloadReferenceComparer()
    {
    }

    public bool Equals(ResoniteTexturePayload? x, ResoniteTexturePayload? y) => ReferenceEquals(x, y);

    public int GetHashCode(ResoniteTexturePayload obj) => RuntimeHelpers.GetHashCode(obj);
}
