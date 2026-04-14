using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class CommonMaterialPrewarmSession(Action<string>? progressReporter = null) : IAsyncDisposable
{
    private readonly Action<string>? progressReporter = progressReporter;
    private Task runningTask = Task.CompletedTask;
    private CancellationTokenSource? cancellation;

    public void Start(
        IResoniteConstructionSource source,
        IResoniteSceneBuilder sceneBuilder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sceneBuilder);

        if (cancellation is not null)
        {
            throw new InvalidOperationException("Common material prewarm has already started.");
        }

        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runningTask = RunAsync(source, sceneBuilder, cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync();
        if (!runningTask.IsCompleted)
        {
            _ = runningTask.ContinueWith(
                static completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        else if (runningTask.IsFaulted)
        {
            _ = runningTask.Exception;
        }

        cancellation.Dispose();
        cancellation = null;
        runningTask = Task.CompletedTask;
    }

    public Task WhenCompletedAsync()
    {
        return runningTask;
    }

    private async Task RunAsync(
        IResoniteConstructionSource source,
        IResoniteSceneBuilder sceneBuilder,
        CancellationToken cancellationToken)
    {
        int preparedCommonMaterialCount = 0;
        await foreach (ResoniteMaterialBinding material in source.ReadCommonMaterialsAsync(cancellationToken))
        {
            await sceneBuilder.PrepareCommonMaterialAsync(material, cancellationToken);
            preparedCommonMaterialCount++;
        }

        if (preparedCommonMaterialCount > 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Debug("import", $"Prepared {preparedCommonMaterialCount} Common materials during source parsing."));
        }
    }
}
