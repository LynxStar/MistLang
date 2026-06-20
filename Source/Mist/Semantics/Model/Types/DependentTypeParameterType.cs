using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a dependent type parameter (e.g., <c>Size</c> in <c>class FixedBuffer&lt;Size:int&gt;</c>).
/// Unlike regular type parameters which represent types, dependent type parameters represent
/// compile-time constant values of a specified primitive type.
/// </summary>
public sealed class DependentTypeParameterType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Struct;
    public string Name { get; }

    /// <summary>
    /// Position in the type parameter list (0-based).
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// The value type of this dependent parameter (e.g., int, bool, char).
    /// This is the IType representing the primitive type annotation.
    /// Mutable to allow post-pass resolution of named constraints.
    /// </summary>
    public IType ValueType { get; internal set; }

    /// <summary>
    /// Optional adhoc refinement constraint predicate attached via a where clause.
    /// Null if no adhoc refinement constraint exists for this parameter.
    /// </summary>
    public RefinementPredicate? AdhocConstraint { get; set; }

    /// <summary>
    /// The resolved refinement type constraint (e.g., <c>ZeroOrPositiveInt</c>).
    /// Non-null when this dependent parameter uses a named refinement type as its constraint
    /// rather than a bare primitive keyword.
    /// </summary>
    public RefinementType? RefinementConstraint { get; set; }

    /// <summary>
    /// The name of an unresolved identifier constraint (e.g., "ZeroOrPositiveInt").
    /// Set during Pass 1 when the constraint cannot be resolved yet (type alias
    /// DeclaredType is null). Cleared after post-pass resolution completes.
    /// </summary>
    internal string? PendingConstraintName { get; set; }

    public DependentTypeParameterType(string name, int ordinal, IType valueType)
    {
        Name = name;
        DisplayName = name;
        Ordinal = ordinal;
        ValueType = valueType;
    }
}
