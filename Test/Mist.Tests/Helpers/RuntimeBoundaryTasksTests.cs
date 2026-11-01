namespace Mist.Tests.Helpers;

[Trait("Tier", "1")]
public sealed class RuntimeBoundaryTasksTests
{
    [Fact]
    public async Task ReadinessDoesNotRequireWorkToFinish()
    {
        var work = new TaskCompletionSource();

        await RuntimeBoundaryTasks.WaitUntilReadyAsync(Task.CompletedTask, work.Task, CancellationToken.None);

        Assert.False(work.Task.IsCompleted);
    }

    [Fact]
    public async Task AWorkFaultBeforeReadinessKeepsItsOriginalException()
    {
        var ready = new TaskCompletionSource();
        var work = new TaskCompletionSource();
        var failure = new InvalidOperationException("child failure");
        var pending = RuntimeBoundaryTasks.WaitUntilReadyAsync(ready.Task, work.Task, CancellationToken.None);

        work.SetException(failure);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task AWorkFaultIsNotHiddenByAnAlreadyCompletedSignal()
    {
        var failure = new InvalidOperationException("child failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeBoundaryTasks.WaitUntilReadyAsync(Task.CompletedTask, Task.FromException(failure), CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task WorkThatExitsWithoutSignallingIsRefused()
    {
        var ready = new TaskCompletionSource();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeBoundaryTasks.WaitUntilReadyAsync(ready.Task, Task.CompletedTask, CancellationToken.None));

        Assert.Equal("Work completed without signalling readiness.", failure.Message);
    }

    [Fact]
    public async Task AReadinessFaultKeepsItsOriginalException()
    {
        var work = new TaskCompletionSource();
        var failure = new InvalidOperationException("signal failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeBoundaryTasks.WaitUntilReadyAsync(Task.FromException(failure), work.Task, CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task UnfinishedCleanupReportsBothItsLimitAndTheOriginalFailure()
    {
        var work = new TaskCompletionSource();
        var failure = new InvalidOperationException("probe failure");

        var actual = await Assert.ThrowsAsync<AggregateException>(() =>
            RuntimeBoundaryTasks.DrainAsync(work.Task, TimeSpan.Zero, failure));

        Assert.Same(failure, actual.InnerExceptions[0]);
        var timeout = Assert.IsType<TimeoutException>(actual.InnerExceptions[1]);
        Assert.Equal("Runtime-boundary cleanup timed out with unfinished work.", timeout.Message);
        Assert.False(work.Task.IsCompleted);
    }

    [Fact]
    public async Task ACompletedChildFaultTakesPrecedenceOverParentCancellation()
    {
        using var lifetime = new CancellationTokenSource();
        await lifetime.CancelAsync();
        var ready = new TaskCompletionSource();
        var failure = new InvalidOperationException("child failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeBoundaryTasks.WaitUntilReadyAsync(ready.Task, Task.FromException(failure), lifetime.Token));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task TheParentCanCancelAnUnsignalledWait()
    {
        using var lifetime = new CancellationTokenSource();
        var ready = new TaskCompletionSource();
        var work = new TaskCompletionSource();
        var pending = RuntimeBoundaryTasks.WaitUntilReadyAsync(ready.Task, work.Task, lifetime.Token);

        await lifetime.CancelAsync();

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(lifetime.Token, failure.CancellationToken);
    }
}