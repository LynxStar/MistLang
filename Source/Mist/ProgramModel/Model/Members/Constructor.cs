using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A constructor declared on a type. Constructors are methods whose return
/// type is the declaring type and whose name is the type name.
/// </summary>
internal sealed class Constructor(
    string name,
    IMistType returnType,
    List<Parameter> parameters,
    MemberModifiers modifiers)
    : Method(name, returnType, parameters, modifiers), IConstructor
{
    /// <summary>
    /// Which constructor this one delegates to before its own body runs.
    /// </summary>
    public ConstructorChainKind ChainKind { get; init; }

    /// <summary>
    /// The declaring chain syntax, retained so the arguments can be bound in Phase 3
    /// alongside the body. They reference the constructor's parameters, so they cannot be
    /// bound earlier. Null when <see cref="ChainKind"/> is
    /// <see cref="ConstructorChainKind.None"/>.
    /// </summary>
    public ConstructorChainNode? ChainNode { get; init; }

    /// <summary>
    /// The bound arguments passed to the chained constructor, populated in Phase 3.
    /// </summary>
    public List<Expression> ChainArgumentsList { get; } = [];
}