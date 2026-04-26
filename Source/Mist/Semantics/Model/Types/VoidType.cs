using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// The void type. Void is a unit struct.
/// </summary>
public sealed class VoidType : IType
{
    public static readonly VoidType Instance = new();
    public string DisplayName => "void";
    public TypeKind Kind => TypeKind.Struct;

    private VoidType() { }
}
