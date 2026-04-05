using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportRequestValidator
{
    public static IReadOnlyList<string> Validate(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(request.Dataset))
        {
            errors.Add("The dataset value is required.");
        }

        if (string.IsNullOrWhiteSpace(request.MeshCode))
        {
            errors.Add("The mesh code value is required.");
        }

        switch (request.SourceKind)
        {
            case DatasetSourceKind.Local:
                if (string.IsNullOrWhiteSpace(request.InputPath))
                {
                    errors.Add("The --input value is required when --source local is used.");
                    break;
                }

                if (!Directory.Exists(request.InputPath))
                {
                    errors.Add($"The local dataset root '{request.InputPath}' does not exist.");
                }

                break;
            case DatasetSourceKind.Server:
                if (request.ServerUri is not null && !request.ServerUri.IsAbsoluteUri)
                {
                    errors.Add("The --server-url value must be an absolute URI.");
                }

                break;
        }

        return errors;
    }
}
