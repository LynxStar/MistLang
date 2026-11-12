using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An associated type slot on an abstract type: a <see cref="TypeParameter"/> closed by conformance (an
/// implementing type supplies it) rather than by a type argument at the use site, and which, unlike a
/// plain type parameter, may declare its own type parameters.
/// <para>
/// The slot's own <c>:</c> bound is held on the inherited <see cref="TypeParameter.ConstraintsList"/>;
/// <c>where</c> constraints on its type parameters are held on those parameters. Registered by simple
/// name in the declaring abstract's scope.
/// </para>
/// </summary>
internal sealed class TypeSlot : TypeParameter, ITypeSlot, IHasTypeParameters
{
    public TypeSlot(string name, int ordinal, Scope declaringScope, MistType? declaringType)
        : base(name, ordinal, declaringScope, declaringType)
    {
    }

    public List<TypeParameter> TypeParametersList { get; } = [];

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;
}