using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An operator declared on a type.
/// </summary>
internal sealed class OperatorDefinition(
    string operatorToken,
    ITypeReference returnType,
    IReadOnlyList<IParameterDefinition> parameters) : IOperatorDefinition
{
    public string OperatorToken { get; } = operatorToken;

    public ITypeReference ReturnType { get; } = returnType;

    public IReadOnlyList<IParameterDefinition> Parameters { get; } = parameters;
}