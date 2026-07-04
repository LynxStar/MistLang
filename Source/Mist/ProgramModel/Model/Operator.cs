using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An operator declared on a type.
/// </summary>
internal sealed class Operator(
    OperatorKind kind,
    IMistType returnType,
    IReadOnlyList<IParameter> parameters,
    MemberModifiers modifiers) : IOperator
{
    public OperatorKind Kind { get; } = kind;

    public IMistType ReturnType { get; } = returnType;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;

    public bool IsUnary => Parameters.Count == 1;

    public Block? Body { get; init; }
}