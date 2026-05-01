using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// An array type. Arrays are classes (reference types) in .NET.
/// </summary>
// v1 decision: ArrayType is kept as a structural marker representing
// Standard.Array<T>. Folding into ConstructedType(Standard.Array<T>)
// is deferred to v1.x. The CSharpTypeMap handles ArrayType by
// recursively mapping the element type and appending "[]".
public sealed class ArrayType : IType
{
    public IType ElementType { get; }
    public string DisplayName => $"{ElementType.DisplayName}[]";
    public TypeKind Kind => TypeKind.Class;

    public ArrayType(IType elementType) { ElementType = elementType; }
}
