using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class PlateauImportRequestValidatorTests
{
    [Fact]
    public void ValidateRequiresServerUrlForRemoteSource()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Remote,
            LocalSourcePath: null,
            ServerUri: null);

        IReadOnlyList<string> errors = PlateauImportRequestValidator.Validate(request);

        Assert.Contains(
            errors,
            error => string.Equals(
                error,
                "The --server-url value is required when --source remote is used.",
                StringComparison.Ordinal));
    }
}
