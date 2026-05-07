using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a generic trait with all type parameters substituted by concrete type arguments
/// (e.g., <c>Comparable&lt;int&gt;</c>, <c>Converter&lt;string, int&gt;</c>).
/// Analogous to <see cref="ConstructedType"/> for classes/structs/interfaces.
/// </summary>
public sealed class ConstructedTraitType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Trait;

    /// <summary>The unparameterized generic trait definition.</summary>
    public TraitType GenericTraitDefinition { get; }

    /// <summary>The concrete type arguments substituted for the type parameters.</summary>
    public IReadOnlyList<IType> TypeArguments { get; }

    /// <summary>The member scope of the generic definition (members resolved with substitution).</summary>
    public IScope MemberScope => GenericTraitDefinition.MemberScope;

    public ConstructedTraitType(TraitType genericTraitDefinition, IReadOnlyList<IType> typeArguments)
    {
        GenericTraitDefinition = genericTraitDefinition;
        TypeArguments = typeArguments;

        var argNames = string.Join(", ", typeArguments.Select(a => a.DisplayName));
        DisplayName = $"{genericTraitDefinition.DisplayName}<{argNames}>";
    }
}
