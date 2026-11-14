using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

internal sealed class Newtype(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, NewtypeDeclarationNode declarationNode)
    : DeclaredType(name, namespaceName, parentScope, declaringType), INewtype, IHasTypeParameters
{
    internal NewtypeDeclarationNode DeclarationNode { get; } = declarationNode;

    public List<TypeParameter> TypeParametersList { get; } = [];

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;

    public MemberModifiers Modifiers { get; internal set; }

    public IMistType? SourceType { get; internal set; }

    /// <summary>
    /// The synthesized <c>Value</c> field exposing the wrapped representation. Present only
    /// when the source is intrinsic: an intrinsic declares no Mist members to forward, so this
    /// field is the wrapper's one bridge to its value, and the emitted wrapper struct carries
    /// it. A user-defined source forwards its own members instead and emits as a duplicate
    /// that stores no separate value.
    /// </summary>
    internal Field? ValueField { get; set; }

    IMistType? INewtype.SourceType => SourceType;
}