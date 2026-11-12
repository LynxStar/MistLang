using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Construction.Members;

internal readonly record struct ResolvedDelegate(
    MemberModifiers Modifiers,
    List<Parameter> Parameters,
    Block? Body);

internal readonly record struct ResolvedFullDelegate(
    ResolvedDelegate Base,
    List<ContextBinding> ContextBindings,
    ThisTier? DeclaredThisTier)
{
    public MemberModifiers Modifiers => Base.Modifiers;
    public List<Parameter> Parameters => Base.Parameters;
    public Block? Body => Base.Body;
}