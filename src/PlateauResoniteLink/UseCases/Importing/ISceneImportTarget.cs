namespace PlateauResoniteLink.Application.Importing;

public interface ISceneImportTarget : IAsyncDisposable
{
    Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default);
}
