using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A method declared on a type.
/// </summary>
public interface IMethod : IGenericDeclaration
{
    /// <summary>The method name.</summary>
    string Name { get; }

    /// <summary>The resolved return type.</summary>
    IMistType ReturnType { get; }

    /// <summary>The method parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }

    /// <summary>The modifiers declared on this method.</summary>
    MemberModifiers Modifiers { get; }

    /// <summary>
    /// Context bindings declared via <c>with Type name</c> clauses on this method.
    /// Empty if no context bindings are declared.
    /// </summary>
    IReadOnlyList<IContextBinding> ContextBindings { get; }

    /// <summary>
    /// The ownership tier required on 'this' declared via a <c>with {tier}</c> clause,
    /// or null if no this-tier clause is present.
    /// </summary>
    ThisTier? DeclaredThisTier { get; }
}