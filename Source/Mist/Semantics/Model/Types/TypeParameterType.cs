using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents an unsubstituted type parameter (e.g., <c>T</c> in <c>class Box&lt;T&gt;</c>).
/// Type parameters are treated as reference types by default (Kind = TypeKind.Class).
/// </summary>
public sealed class TypeParameterType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Class;
    public string Name { get; }

    /// <summary>
    /// Position in the type parameter list (0-based).
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// The constraints declared on this type parameter.
    /// Null if no constraints are declared (unconstrained).
    /// </summary>
    public TypeParameterConstraints? Constraints { get; internal set; }

    public TypeParameterType(string name, int ordinal)
    {
        Name = name;
        DisplayName = name;
        Ordinal = ordinal;
    }
}
