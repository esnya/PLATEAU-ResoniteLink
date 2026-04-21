using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

internal static class CliDefaultOptions
{
    public const int ResoniteLinkConnectionCount = 4;
    public const PlateauImportMemoryProfile MemoryProfile = PlateauImportMemoryProfile.Large;

    public static readonly string[] PackageNames =
    [
        "dem",
        "bldg",
        "brid",
        "frn",
        "tran",
        "rwy",
        "trk",
        "tun",
        "ubld",
        "unf",
        "veg",
    ];
}
