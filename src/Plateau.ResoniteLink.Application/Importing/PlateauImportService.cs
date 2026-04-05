using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    IResoniteSceneBuilder sceneBuilder,
    IPlateauDatasetSourceResolver? datasetSourceResolver = null)
{
    private readonly IResoniteSceneBuilder sceneBuilder = sceneBuilder;
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? new CkanPlateauDatasetSourceResolver();

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        IReadOnlyList<string> validationErrors = PlateauImportRequestValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            throw new PlateauImportValidationException(validationErrors);
        }

        PlateauImportRequest normalizedRequest = request with
        {
            Dataset = request.Dataset.Trim(),
            MeshCode = request.MeshCode.Trim(),
            LocalSourcePath = string.IsNullOrWhiteSpace(request.LocalSourcePath) ? null : request.LocalSourcePath.Trim(),
        };

        PlateauImportRequest resolvedRequest =
            await datasetSourceResolver.ResolveAsync(normalizedRequest, workRoot, cancellationToken);

        IResoniteConstructionSource source = LocalCityGmlResonitePlanBuilder.CreateConstructionSource(resolvedRequest);

        try
        {
            await sceneBuilder.BeginAsync(source.Metadata, workRoot, cancellationToken);
            bool processedAnyCityObject = false;

            await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync(cancellationToken))
            {
                processedAnyCityObject = true;
                await sceneBuilder.ProcessCityObjectAsync(cityObject, cancellationToken);
            }

            if (!processedAnyCityObject)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            IReadOnlyList<string> destinations = await sceneBuilder.CompleteAsync(cancellationToken);
            return new ImportExecutionResult(source.Metadata, destinations);
        }
        finally
        {
            await sceneBuilder.DisposeAsync();
        }
    }
}
