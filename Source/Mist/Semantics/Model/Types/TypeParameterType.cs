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

    /// <summary>
    /// True when this type parameter is a variadic pack (declared <c>params TArgs</c>
    /// or <c>Type[]:TArgs</c> / <c>Type[N]:TArgs</c>). A pack stands for an ordered,
    /// comptime-known list of type arguments; its value domain is the metatype
    /// array <c>Type[]</c>. Everything else about it (name, ordinal, constraints) is
    /// an ordinary type parameter, which is what lets a pack reuse the generics
    /// machinery.
    /// </summary>
    public bool IsPack { get; init; }

    /// <summary>
    /// True when this pack is the single bracket-free (unbracketed) pack in its list —
    /// declared with <c>params</c>, or a lone <c>Type[]:</c> pack promoted by inverse
    /// sugar. The unbracketed pack absorbs the contiguous middle arguments at the use
    /// site; any other pack in the list must be supplied bracketed. Only meaningful
    /// when <see cref="IsPack"/> is true.
    /// </summary>
    public bool IsUnbracketedPack { get; init; }

    /// <summary>
    /// For a fixed-size pack (<c>Type[N]:TArgs</c>), the exact arity <c>N</c>. Null for
    /// an unknown-length pack (<c>Type[]:TArgs</c> / <c>params TArgs</c>). Only
    /// meaningful when <see cref="IsPack"/> is true.
    /// </summary>
    public int? PackFixedArity { get; init; }

    public TypeParameterType(string name, int ordinal)
    {
        Name = name;
        DisplayName = name;
        Ordinal = ordinal;
    }
}