using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// Its return type is the declaring type and its name the type name.
/// </summary>
internal sealed class Constructor(
    string name,
    IMistType returnType,
    List<Parameter> parameters,
    MemberModifiers modifiers)
    : Method(name, returnType, parameters, modifiers), IConstructor
{
    public ConstructorChainKind ChainKind { get; init; }

    /// <summary>
    /// Kept so the chain's arguments bind in Phase 3 with the body: they reference the constructor's
    /// parameters. Null when <see cref="ChainKind"/> is <see cref="ConstructorChainKind.None"/>.
    /// </summary>
    public ConstructorChainNode? ChainNode { get; init; }

    public List<Expression> ChainArgumentsList { get; } = [];

    /// <summary>
    /// Which constructor the chain's arguments selected, and the type each was bound against, for
    /// the reason <see cref="InvocationExpression.Method"/> records its selection. Null
    /// when there is no chain, or the chained type names no constructor to choose between.
    /// </summary>
    public Constructor? ChainedConstructor { get; internal set; }

    public IReadOnlyList<IMistType>? ChainParameterTypes { get; internal set; }
}