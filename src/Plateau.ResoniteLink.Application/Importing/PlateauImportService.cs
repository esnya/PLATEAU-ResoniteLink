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
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        IReadOnlyList<string> validationErrors = PlateauImportRequestValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            throw new PlateauImportValidationException(validationErrors);
        }

        PlateauImportRequest normalizedRequest = request with
        {
            Dataset = request.Dataset.Trim(),
            MeshCode = request.MeshCode.Trim(),
            InputPath = string.IsNullOrWhiteSpace(request.InputPath) ? null : request.InputPath.Trim(),
        };

        PlateauImportRequest resolvedRequest =
            await datasetSourceResolver.ResolveAsync(normalizedRequest, outputRoot, cancellationToken);

        IResoniteConstructionSource source = LocalCityGmlResonitePlanBuilder.CreateConstructionSource(resolvedRequest);

        try
        {
            await sceneBuilder.BeginAsync(source.Metadata, outputRoot, cancellationToken);
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
