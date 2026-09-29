namespace Mist.Backends.Contracts;

/// <summary>
/// A unique identifier for a compiler backend.
/// </summary>
public readonly record struct BackendId(string Value)
{
    public static BackendId CSharp { get; } = new("csharp");
}