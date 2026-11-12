using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type written in source or projected from an assembly: reachable through a namespace, registered
/// by fully qualified name, and OWNING a scope for the names declared inside it. Structural types --
/// tuples, unions, refinements, constructed types, the error and unknown sentinels -- are computed, so
/// nothing is declared inside them or addresses them by name; a <see cref="TypeParameter"/> borrows
/// the scope of the declaration that introduced it.
/// </summary>
internal abstract class DeclaredType : MistType
{
    protected DeclaredType(
        string name,
        string namespaceName,
        Scope? parentScope,
        MistType? declaringType)
        : base(name, Qualify(name, namespaceName, declaringType), declaringType, scope => new TypeScope(parentScope, scope))
    {
        NamespaceName = namespaceName;
    }

    /// <summary>
    /// Empty for the global namespace.
    /// </summary>
    public string NamespaceName { get; }

    /// <summary>
    /// The CLR type this declaration is at runtime, by metadata name, where the ambient provider knows
    /// it: a projected type's own, or the one a Standard intrinsic emits as. Two declarations naming one
    /// runtime type are one type to <see cref="Construction.Binding.ConversionRelation"/>.
    /// </summary>
    public string? ClrMetadataName { get; internal set; }

    /// <summary>
    /// Nesting wins over the namespace: a nested type hangs off its declaring type, whose own
    /// qualified name already carries the namespace.
    /// </summary>
    private static string Qualify(string name, string namespaceName, MistType? declaringType)
    {
        if(declaringType is not null)
        {
            return Nest(name, declaringType);
        }

        return string.IsNullOrEmpty(namespaceName)
            ? name
            : $"{namespaceName}.{name}"
            ;
    }

    /// <summary>
    /// The base declares it as a plain <see cref="Scope"/> because a borrower's need not be a
    /// <see cref="TypeScope"/>.
    /// </summary>
    public TypeScope TypeScope => (TypeScope)Scope;
}