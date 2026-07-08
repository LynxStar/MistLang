using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

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

    /// <summary>The modifiers declared on this method.</summary>
    MemberModifiers Modifiers { get; }

    /// <summary>
    /// The type parameters declared on this method. Empty for non-generic methods.
    /// </summary>
    IReadOnlyList<ITypeParameter> TypeParameters { get; }
}