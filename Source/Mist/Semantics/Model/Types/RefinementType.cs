using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A refinement type that constrains the valid values of a base type via a
/// pattern-match predicate. For example, <c>type AdultAge = int >= 18;</c>
/// defines a refinement type where only integers greater than or equal to 18
/// are valid.
/// </summary>
public sealed class RefinementType : IType
{
    /// <summary>
    /// The alias name (e.g., "AdultAge").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The underlying type (e.g., IntType).
    /// </summary>
    public IType BaseType { get; }

    /// <summary>
    /// The constraint predicate that values must satisfy.
    /// </summary>
    public RefinementPredicate Predicate { get; }

    public string DisplayName => Name;

    public TypeKind Kind => BaseType.Kind;

    public RefinementType(string name, IType baseType, RefinementPredicate predicate)
    {
        Name = name;
        BaseType = baseType;
        Predicate = predicate;
    }
}