using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// The metatype. Represents the type of type literal expressions.
/// Type is a class (reference type) because System.Type is a reference type in .NET.
/// </summary>
public sealed class TypeType : IType
{
    public static readonly TypeType Instance = new();
    public string DisplayName => "Type";
    public TypeKind Kind => TypeKind.Class;

    private TypeType() { }
}
