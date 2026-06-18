using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteImportStepTaskCleanup
{
    public static async Task CancelAndObserveFailuresAsync(
        CancellationTokenSource cancellation,
        IEnumerable<Task> tasksToObserve)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        ArgumentNullException.ThrowIfNull(tasksToObserve);

        try
        {
            await cancellation.CancelAsync();
        }
        catch (AggregateException)
        {
            // Cancellation callbacks may throw; preserve the primary import failure and continue cleanup.
        }

        await ObserveTaskFailuresAsync(tasksToObserve);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup should observe and suppress orphaned import task failures after the primary send failure.")]
    private static async Task ObserveTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static Task ObserveTaskFailuresAsync(IEnumerable<Task> tasks)
    {
        return Task.WhenAll(tasks.Select(ObserveTaskFailureAsync));
    }
}
