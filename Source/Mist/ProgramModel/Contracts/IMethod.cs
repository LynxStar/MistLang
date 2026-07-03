using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A method declared on a type.
/// </summary>
public interface IMethod
{
    /// <summary>The method name.</summary>
    string Name { get; }

    /// <summary>The resolved return type.</summary>
    IMistType ReturnType { get; }

    /// <summary>The method parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }

    /// <summary>Whether the method is static.</summary>
    bool IsStatic { get; }

    /// <summary>Whether the method is async.</summary>
    bool IsAsync { get; }
}