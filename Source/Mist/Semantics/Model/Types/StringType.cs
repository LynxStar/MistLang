using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// The string type. String is a class (reference type) in .NET.
/// </summary>
public sealed class StringType : IType
{
    public static readonly StringType Instance = new();
    public string DisplayName => "string";
    public TypeKind Kind => TypeKind.Class;

    private StringType() { }
}
