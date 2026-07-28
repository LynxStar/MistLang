using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An associated type slot declared on an abstract type. A slot is a more
/// specific <see cref="TypeParameter"/>: it is closed by conformance (an
/// implementing type supplies the concrete type) rather than by a type argument
/// at the use site, and — unlike a plain type parameter — it may declare its own
/// type parameters.
///
/// The slot's own constraints (the <c>:</c> bound, e.g. <c>: IEnumerator&lt;T&gt;</c>)
/// are held on the inherited <see cref="TypeParameter.ConstraintsList"/>.
/// Constraints on the slot's own type parameters (from <c>where</c> clauses) are
/// held on those parameters. Registered by simple name in its declaring abstract's
/// scope so members can reference it.
/// </summary>
internal sealed class TypeSlot : TypeParameter, ITypeSlot
{
    public TypeSlot(string name, int ordinal, Scope declaringScope, MistType? declaringType)
        : base(name, ordinal, declaringScope, declaringType)
    {
    }

    /// <summary>
    /// Type parameters declared on the slot itself (e.g. <c>T</c> in
    /// <c>type Foo&lt;T&gt;;</c>). Empty for a non-generic slot.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    IReadOnlyList<ITypeParameter> ITypeSlot.TypeParameters => TypeParametersList;
}