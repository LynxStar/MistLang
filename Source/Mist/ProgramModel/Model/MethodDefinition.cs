using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A method declared on a type.
/// </summary>
internal sealed class MethodDefinition(
    string name,
    ITypeReference returnType,
    IReadOnlyList<IParameterDefinition> parameters,
    bool isStatic,
    bool isAsync) : IMethodDefinition
{
    public string Name { get; } = name;

    public ITypeReference ReturnType { get; } = returnType;

    public IReadOnlyList<IParameterDefinition> Parameters { get; } = parameters;

    public bool IsStatic { get; } = isStatic;

    public bool IsAsync { get; } = isAsync;
}