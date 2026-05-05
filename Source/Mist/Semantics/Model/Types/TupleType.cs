using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A structural value type representing a fixed-length tuple of element types.
/// Analogous to ArrayType as a structural type marker — not folded into
/// ConstructedType. The C# backend maps this to System.ValueTuple&lt;...&gt;.
/// </summary>
public sealed class TupleType : IType
{
    /// <summary>
    /// The ordered element types of this tuple.
    /// </summary>
    public IReadOnlyList<IType> ElementTypes { get; }

    /// <summary>
    /// Optional element names (parallel to ElementTypes).
    /// Null entries indicate unnamed elements. The entire list is null
    /// if no elements are named.
    /// </summary>
    public IReadOnlyList<string?>? ElementNames { get; }

    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Struct;

    public TupleType(IReadOnlyList<IType> elementTypes, IReadOnlyList<string?>? elementNames = null)
    {
        ElementTypes = elementTypes;
        ElementNames = elementNames;

        // Build display name: "(int, string)" or "(int x, string y)"
        var parts = new string[elementTypes.Count];
        for (var i = 0; i < elementTypes.Count; i++)
        {
            var name = elementNames is not null && i < elementNames.Count ? elementNames[i] : null;
            parts[i] = name is not null
                ? $"{elementTypes[i].DisplayName} {name}"
                : elementTypes[i].DisplayName;
        }
        DisplayName = $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Returns the number of elements in this tuple.
    /// </summary>
    public int Arity => ElementTypes.Count;
}
