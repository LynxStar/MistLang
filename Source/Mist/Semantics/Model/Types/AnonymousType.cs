using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A compiler-generated, immutable reference type whose shape is defined by the
/// properties in a <c>new { ... }</c> expression. The type has no user-declared name
/// and is identified structurally by its ordered property names and types.
/// </summary>
public sealed class AnonymousType : IType
{
    /// <summary>
    /// Ordered property names.
    /// </summary>
    public IReadOnlyList<string> PropertyNames { get; }

    /// <summary>
    /// Parallel list of resolved property types.
    /// </summary>
    public IReadOnlyList<IType> PropertyTypes { get; }

    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Class;

    public AnonymousType(IReadOnlyList<string> propertyNames, IReadOnlyList<IType> propertyTypes)
    {
        PropertyNames = propertyNames;
        PropertyTypes = propertyTypes;

        // Build display: "{ Name: string, Version: int }"
        var parts = new string[propertyNames.Count];
        for (var i = 0; i < propertyNames.Count; i++)
            parts[i] = $"{propertyNames[i]}: {propertyTypes[i].DisplayName}";
        DisplayName = $"{{ {string.Join(", ", parts)} }}";
    }

    /// <summary>
    /// Two AnonymousTypes are structurally equal if they have the same
    /// property names, types, and order.
    /// </summary>
    public bool StructurallyEquals(AnonymousType other)
    {
        if (PropertyNames.Count != other.PropertyNames.Count) return false;
        for (var i = 0; i < PropertyNames.Count; i++)
        {
            if (PropertyNames[i] != other.PropertyNames[i]) return false;
            if (PropertyTypes[i] != other.PropertyTypes[i]) return false;
        }
        return true;
    }
}
