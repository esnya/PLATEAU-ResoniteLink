using System;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Cli;

internal static class CliResoniteTargetOptions
{
    public static ResoniteImportMemoryProfile MapMemoryProfile(
        PlateauImportMemoryProfile memoryProfile,
        string parameterName)
    {
        return memoryProfile switch
        {
            PlateauImportMemoryProfile.Small => ResoniteImportMemoryProfile.Small,
            PlateauImportMemoryProfile.Large => ResoniteImportMemoryProfile.Large,
            _ => throw new ArgumentOutOfRangeException(parameterName, memoryProfile, "Unsupported memory profile."),
        };
    }
}
