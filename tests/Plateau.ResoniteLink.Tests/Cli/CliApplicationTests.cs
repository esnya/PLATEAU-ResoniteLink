using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsyncWritesArtifactForValidBuildCommand()
    {
        using TemporaryDirectory tempDirectory = new();
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        CliApplication application = new(
            standardOutput,
            standardError,
            new PlateauImportService(new JsonArtifactResoniteSceneBuilder()));

        int exitCode = await application.RunAsync(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "local",
                "--input",
                fixturePath,
                "--output-root",
                tempDirectory.Path,
            ]);

        string artifactPath = Path.Combine(
            tempDirectory.Path,
            "tokyo23ku",
            "53394525",
            "resonite-construction-plan.json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(artifactPath));
        Assert.Contains("Resonite construction plan generated.", standardOutput.ToString());
        Assert.Contains($"Destination: {artifactPath}", standardOutput.ToString());
        Assert.Equal(string.Empty, standardError.ToString());
    }
}
