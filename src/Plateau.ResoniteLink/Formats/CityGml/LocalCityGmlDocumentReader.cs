using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
{
    public async Task<LocalCityGmlDocumentSet> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
    }
}
