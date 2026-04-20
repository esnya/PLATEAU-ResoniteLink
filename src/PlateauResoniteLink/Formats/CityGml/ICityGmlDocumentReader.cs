using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public interface ICityGmlDocumentReader
{
    Task<LocalCityGmlDocumentReadResult> ReadAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default);
}
