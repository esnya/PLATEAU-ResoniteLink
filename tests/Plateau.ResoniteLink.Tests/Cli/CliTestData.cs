namespace Plateau.ResoniteLink.Tests.Cli;

internal static class CliTestData
{
    public static readonly string[] DocumentedDefaultPackageNames =
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

    public static string[] BuildLocalBuildArgs(string fixturePath)
    {
        return
        [
            "build",
            "--dataset",
            "tokyo23ku",
            "--mesh-code",
            "53394525",
            "--source",
            "local",
            "--local-source-path",
            fixturePath,
            "--resonitelink-port",
            "12345",
        ];
    }
}
