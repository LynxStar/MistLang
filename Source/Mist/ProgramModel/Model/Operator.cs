using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An operator declared on a type.
/// </summary>
internal sealed class Operator(
    string operatorToken,
    IMistType returnType,
    IReadOnlyList<IParameter> parameters) : IOperator
{
    public string OperatorToken { get; } = operatorToken;

    public IMistType ReturnType { get; } = returnType;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;
}