using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A type written in source or projected from an assembly: one that is reachable through a
/// namespace, registered by fully qualified name, and OWNS a scope for the names declared inside
/// it. Classes, structs, abstracts, enums, newtypes and type aliases.
/// <para>
/// The distinction this draws is against the two kinds of <see cref="MistType"/> that are not
/// declared. Structural types -- tuples, unions, refinements, constructed types, the error and
/// unknown sentinels -- are computed rather than written, so nothing is declared inside them and
/// nothing addresses them by name. And a <see cref="TypeParameter"/> BORROWS the scope of the
/// declaration that introduced it rather than owning one.
/// </para>
/// <para>
/// Owning the scope is what lets both sides be readonly. This constructor takes the PARENT scope
/// and builds the <see cref="TypeScope"/> around itself, so the type knows its scope and the scope
/// knows its type from the moment either exists -- where passing a scope in meant one of the two
/// had to be filled in afterwards.
/// </para>
/// </summary>
internal abstract class DeclaredType : MistType
{
    protected DeclaredType(
        string name,
        string namespaceName,
        Scope? parentScope,
        MistType? declaringType)
        : base(name, namespaceName, declaringType, scope => new TypeScope(parentScope, scope))
    {
    }

    /// <summary>
    /// This type's own scope, known to hold this type. The base declares it as a plain
    /// <see cref="Scope"/> because a borrower's need not be a <see cref="TypeScope"/>.
    /// </summary>
    public TypeScope TypeScope => (TypeScope)Scope;
}