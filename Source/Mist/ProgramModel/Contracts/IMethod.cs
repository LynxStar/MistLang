using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Contracts;

public interface IMethod : IGenericDeclaration
{
    string Name { get; }

    IMistType ReturnType { get; }

    IReadOnlyList<IParameter> Parameters { get; }

    MemberModifiers Modifiers { get; }

    IReadOnlyList<IContextBinding> ContextBindings { get; }

    /// <summary>The ownership tier a <c>with {tier}</c> clause requires of <c>this</c>.</summary>
    ThisTier? DeclaredThisTier { get; }
}