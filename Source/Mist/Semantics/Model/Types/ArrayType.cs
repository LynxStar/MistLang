using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// An array type. Arrays are classes (reference types) in .NET.
/// </summary>
public sealed class ArrayType : IType
{
    public IType ElementType { get; }
    public string DisplayName => $"{ElementType.DisplayName}[]";
    public TypeKind Kind => TypeKind.Class;

    public ArrayType(IType elementType) { ElementType = elementType; }
}
