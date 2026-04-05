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

        ResoniteConstructionPlan plan = LocalCityGmlResonitePlanBuilder.BuildPlan(resolvedRequest);
        IReadOnlyList<string> destinations =
            await sceneBuilder.BuildAsync(plan, outputRoot, cancellationToken);

        return new ImportExecutionResult(plan, destinations);
    }
}
