using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a generic abstract type with all type parameters substituted by concrete type arguments
/// (e.g., <c>Comparable&lt;int&gt;</c>, <c>Converter&lt;string, int&gt;</c>).
/// Analogous to <see cref="ConstructedType"/> for classes/structs/interfaces.
/// </summary>
public sealed class ConstructedAbstractType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Abstract;

    /// <summary>
    /// The unparameterized generic abstract type definition.
    /// </summary>
    public AbstractType GenericAbstractDefinition { get; }

    /// <summary>The concrete type arguments substituted for the type parameters.</summary>
    public IReadOnlyList<IType> TypeArguments { get; }

    /// <summary>The member scope of the generic definition (members resolved with substitution).</summary>
    public IScope MemberScope => GenericAbstractDefinition.MemberScope;

    /// <summary>
    /// The type parameters from the generic definition.
    /// </summary>
    public IReadOnlyList<IType> DefinitionTypeParameters => GenericAbstractDefinition.TypeParameters;

    /// <summary>
    /// Whether the generic definition is part of a cyclic inheritance chain.
    /// </summary>
    public bool IsCyclic => GenericAbstractDefinition.IsCyclic;

    public ConstructedAbstractType(AbstractType genericAbstractDefinition, IReadOnlyList<IType> typeArguments)
    {
        GenericAbstractDefinition = genericAbstractDefinition;
        TypeArguments = typeArguments;

        var argNames = string.Join(", ", typeArguments.Select(a => a.DisplayName));
        DisplayName = $"{genericAbstractDefinition.DisplayName}<{argNames}>";
    }
}
