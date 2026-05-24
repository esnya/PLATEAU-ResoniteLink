using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResonitePackageSemantics
{
    public static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase);
    }
}
