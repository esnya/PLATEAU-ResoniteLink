using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReader : ICityGmlDocumentReader
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
