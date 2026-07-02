using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A method declared on a type.
/// </summary>
public interface IMethodDefinition
{
    /// <summary>The method name.</summary>
    string Name { get; }

    /// <summary>The resolved return type.</summary>
    ITypeReference ReturnType { get; }

    /// <summary>The method parameters.</summary>
    IReadOnlyList<IParameterDefinition> Parameters { get; }

    /// <summary>Whether the method is static.</summary>
    bool IsStatic { get; }

    /// <summary>Whether the method is async.</summary>
    bool IsAsync { get; }
}