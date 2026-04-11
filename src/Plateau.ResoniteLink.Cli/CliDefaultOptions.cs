namespace Plateau.ResoniteLink.Cli;

internal static class CliDefaultOptions
{
    public const int ResoniteLinkConnectionCount = 1;
    public const int ResoniteLinkImportMeshTimeoutMilliseconds = 0;

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
