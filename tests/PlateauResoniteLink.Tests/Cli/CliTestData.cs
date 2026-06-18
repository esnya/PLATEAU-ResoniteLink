
namespace PlateauResoniteLink.Tests.Cli;

internal static class CliTestData
{
    public static string[] CreateLocalImportArgs(string fixturePath)
    {
        return
        [
            "import",
            "--dataset",
            "tokyo23ku",
            "--mesh-code",
            "53394525",
            "--citygml-source",
            fixturePath,
            "--resonitelink-port",
            "12345",
        ];
    }
}
