using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An operator declared on a type.
/// </summary>
internal sealed class Operator(
    string operatorToken,
    ITypeReference returnType,
    IReadOnlyList<IParameter> parameters) : IOperator
{
    public string OperatorToken { get; } = operatorToken;

    public ITypeReference ReturnType { get; } = returnType;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;
}