using System.Diagnostics;
using System.Threading.Channels;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class LiveSendExecutionRuntime : IAsyncDisposable
{
    private readonly Channel<ResoniteLiveSceneImportTarget.QueuedCityObject> cityObjectChannel;
    private readonly CancellationTokenSource processingCancellationSource;
    private readonly TaskCompletionSource<Exception> firstProcessingFailureSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AsyncWeightedGate cityObjectMemoryGate;
    private readonly Stopwatch sceneBuildStopwatch = Stopwatch.StartNew();
    private Task[] processingTasks = [];

    public LiveSendExecutionRuntime(LiveSendQueuePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        cityObjectChannel = Channel.CreateBounded<ResoniteLiveSceneImportTarget.QueuedCityObject>(
            new BoundedChannelOptions(plan.QueueCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        processingCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cityObjectMemoryGate = new AsyncWeightedGate(plan.MemoryBudgetBytes);
    }

    public ChannelReader<ResoniteLiveSceneImportTarget.QueuedCityObject> Reader => cityObjectChannel.Reader;

    public CancellationToken ProcessingCancellationToken => processingCancellationSource.Token;

    public bool IsCancellationRequested => processingCancellationSource.IsCancellationRequested;

    public int ProcessingTaskCount => processingTasks.Length;

    public double ElapsedTotalSeconds => sceneBuildStopwatch.Elapsed.TotalSeconds;

    public void Start(Task[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        processingTasks = tasks;
    }

    public ValueTask<AsyncWeightedGate.Lease> AcquireCityObjectMemoryAsync(long estimatedWorksetBytes, CancellationToken cancellationToken)
    {
        return cityObjectMemoryGate.AcquireAsync(estimatedWorksetBytes, cancellationToken);
    }

    public ValueTask WriteAsync(ResoniteLiveSceneImportTarget.QueuedCityObject queuedCityObject, CancellationToken cancellationToken)
    {
        return cityObjectChannel.Writer.WriteAsync(queuedCityObject, cancellationToken);
    }

    public void CompleteWriter()
    {
        cityObjectChannel.Writer.TryComplete();
    }

    public async Task AwaitCompletionAsync(CancellationToken cancellationToken)
    {
        Task allProcessingTasks = Task.WhenAll(processingTasks);
        Task completedTask = await Task.WhenAny(allProcessingTasks, firstProcessingFailureSource.Task).WaitAsync(cancellationToken);
        if (completedTask == firstProcessingFailureSource.Task)
        {
            Cancel();
            Exception failure = await firstProcessingFailureSource.Task.WaitAsync(cancellationToken);
            throw failure;
        }

        await allProcessingTasks.WaitAsync(cancellationToken);
    }

    public async Task AwaitIfAnyTaskCompletedAsync()
    {
        if (firstProcessingFailureSource.Task.IsCompletedSuccessfully)
        {
            Exception failure = await firstProcessingFailureSource.Task;
            throw failure;
        }

        if (Array.Exists(processingTasks, static task => task.IsCompleted))
        {
            await Task.WhenAll(processingTasks);
        }
    }

    public void TryMarkFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        firstProcessingFailureSource.TrySetResult(exception);
    }

    public void Cancel()
    {
        try
        {
            _ = processingCancellationSource.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        cityObjectChannel.Writer.TryComplete();
        Cancel();

        Task[] drainTasks = processingTasks
            .Select(static task => task.ContinueWith(
                static completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default))
            .ToArray();
        await Task.WhenAll(drainTasks);
        processingCancellationSource.Dispose();
    }
}
