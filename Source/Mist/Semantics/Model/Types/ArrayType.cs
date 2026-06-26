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

    /// <summary>
    /// The array's Length. Null means unknown (unsized parameter, non-literal new T[expr]).
    /// When non-null and SymbolicLengthId is null, this is a concrete length.
    /// </summary>
    public long? KnownLength { get; }

    /// <summary>
    /// A unique identity for symbolic (unknown) lengths. Non-null when the array
    /// has an unknown length that can still be referenced symbolically (e.g., from
    /// a parameter declaration). Two references to the same parameter's .Length
    /// share the same SymbolicLengthId.
    /// </summary>
    public int? SymbolicLengthId { get; }

    public string DisplayName => KnownLength.HasValue
        ? $"{ElementType.DisplayName}[{KnownLength.Value}]"
        : $"{ElementType.DisplayName}[]";
    public TypeKind Kind => TypeKind.Class;

    /// <summary>
    /// Creates an ArrayType with unknown length (backward-compatible constructor).
    /// </summary>
    public ArrayType(IType elementType)
    {
        ElementType = elementType;
        KnownLength = null;
        SymbolicLengthId = null;
    }

    /// <summary>
    /// Creates an ArrayType with a concrete known length.
    /// </summary>
    public ArrayType(IType elementType, long knownLength)
    {
        ElementType = elementType;
        KnownLength = knownLength;
        SymbolicLengthId = null;
    }

    /// <summary>
    /// Creates an ArrayType with a symbolic length identity.
    /// </summary>
    public ArrayType(IType elementType, int symbolicLengthId)
    {
        ElementType = elementType;
        KnownLength = null;
        SymbolicLengthId = symbolicLengthId;
    }
}