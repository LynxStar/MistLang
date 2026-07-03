using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A method declared on a type.
/// </summary>
internal sealed class Method(
    string name,
    ITypeReference returnType,
    IReadOnlyList<IParameter> parameters,
    bool isStatic,
    bool isAsync) : IMethod
{
    public string Name { get; } = name;

    public ITypeReference ReturnType { get; } = returnType;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public bool IsStatic { get; } = isStatic;

    public bool IsAsync { get; } = isAsync;
}