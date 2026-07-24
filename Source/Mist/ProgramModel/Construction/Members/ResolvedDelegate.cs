using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolved output from a <see cref="IDelegateNode"/>: the 4 common pieces
/// shared by all callables (attributes, modifiers, parameters, body).
/// </summary>
internal readonly record struct ResolvedDelegate(
    MemberModifiers Modifiers,
    List<Parameter> Parameters,
    Block? Body);

/// <summary>
/// Resolved output from a <see cref="IFullDelegateNode"/>: the base delegate
/// plus resolved context bindings and an optional this-tier declaration.
/// </summary>
internal readonly record struct ResolvedFullDelegate(
    ResolvedDelegate Base,
    List<ContextBinding> ContextBindings,
    ThisTier? DeclaredThisTier)
{
    public MemberModifiers Modifiers => Base.Modifiers;
    public List<Parameter> Parameters => Base.Parameters;
    public Block? Body => Base.Body;
}