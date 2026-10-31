namespace Mist.Tests.Helpers;

public static class RuntimeBoundaryTasks
{
    public static async Task WaitUntilReadyAsync(Task ready, Task work, TimeSpan timeout)
    {
        _ = await Task.WhenAny(ready, work).WaitAsync(timeout).ConfigureAwait(false);
        if(work.IsCompleted)
        {
            await work.ConfigureAwait(false);
        }

        if(!ready.IsCompleted)
        {
            throw new InvalidOperationException("Work completed without signalling readiness.");
        }

        await ready.ConfigureAwait(false);
    }

    public static async Task DrainAsync(Task work, TimeSpan timeout, Exception? failure)
    {
        // Cleanup observes secondary faults without replacing the probe's original failure.
        await work.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if(!work.IsCompleted)
        {
            var timeoutFailure = new TimeoutException("Runtime-boundary cleanup timed out with unfinished work.");
            throw failure is null ? timeoutFailure : new AggregateException(failure, timeoutFailure);
        }
    }
}