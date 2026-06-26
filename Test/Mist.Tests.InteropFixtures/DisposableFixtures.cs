namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Implements IDisposable directly — used to test basic IDisposable detection.
/// </summary>
public class SimpleDisposable : IDisposable
{
    public void Dispose() { }
}

/// <summary>
/// Implements an interface that extends IDisposable — used to test transitive detection.
/// </summary>
public class TransitiveDisposable : ITransitiveDisposableInterface
{
    public void Dispose() { }
    public void DoWork() { }
}

/// <summary>
/// An interface that extends IDisposable (transitive chain).
/// </summary>
public interface ITransitiveDisposableInterface : IDisposable
{
    void DoWork();
}

/// <summary>
/// Implements IDisposable AND has a public parameterless Drop() method.
/// The explicit Drop() should take precedence (IsDisposableDrop = false).
/// </summary>
public class DisposableWithDrop : IDisposable
{
    public void Dispose() { }
    public void Drop() { }
}

/// <summary>
/// Implements only IAsyncDisposable — should NOT be recognized as droppable.
/// </summary>
public class AsyncOnlyDisposable : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Implements both IDisposable and IAsyncDisposable — should be recognized via IDisposable.
/// </summary>
public class BothDisposables : IDisposable, IAsyncDisposable
{
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A generic type implementing IDisposable — tests that generic definitions get flags set.
/// </summary>
public class GenericDisposable<T> : IDisposable
{
    public T? Value { get; set; }
    public void Dispose() { }
}