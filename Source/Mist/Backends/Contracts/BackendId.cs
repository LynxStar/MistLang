namespace Mist.Backends.Contracts;

public readonly record struct BackendId(string Value)
{
    public static BackendId CSharp { get; } = new("csharp");
}