using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Stores all constraints declared on a single type parameter.
/// </summary>
public sealed class TypeParameterConstraints
{
    /// <summary>Whether the 'class' (reference type) constraint is present.</summary>
    public bool HasClassConstraint { get; init; }

    /// <summary>Whether the 'struct' (value type) constraint is present.</summary>
    public bool HasStructConstraint { get; init; }

    /// <summary>
    /// The base class constraint, if any. At most one is allowed.
    /// </summary>
    public IType? BaseClassConstraint { get; init; }

    /// <summary>
    /// Interface constraints (including trait constraints lowered to interfaces).
    /// </summary>
    public IReadOnlyList<IType> InterfaceConstraints { get; init; } = Array.Empty<IType>();

    /// <summary>
    /// Trait constraints.
    /// </summary>
    public IReadOnlyList<IType> TraitConstraints { get; init; } = Array.Empty<IType>();

    /// <summary>
    /// Constructor constraints. Each entry is a list of parameter types
    /// for one constructor signature. Empty list = new().
    /// Multiple entries use OR semantics.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IType>> ConstructorConstraints { get; init; } = Array.Empty<IReadOnlyList<IType>>();

    /// <summary>Whether the 'readonly' constraint is present.</summary>
    public bool HasReadonlyConstraint { get; init; }

    /// <summary>Whether the 'mutable' constraint is present.</summary>
    public bool HasMutableConstraint { get; init; }

    /// <summary>Whether the 'owned' constraint is present. Type arguments must be owned types.</summary>
    public bool HasOwnedConstraint { get; init; }
}
