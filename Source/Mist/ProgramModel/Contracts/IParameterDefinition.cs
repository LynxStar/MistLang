using Mist.ProgramModel.Contracts.TypeReferences;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A method parameter with its resolved type and modifiers.
/// </summary>
public interface IParameterDefinition
{
    /// <summary>
    /// The parameter name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The resolved type of this parameter.
    /// </summary>
    ITypeReference Type { get; }

    /// <summary>
    /// The passing convention for this parameter.
    /// </summary>
    ParameterPassingKind PassingKind { get; }

    /// <summary>
    /// The ownership tier declared on this parameter.
    /// </summary>
    OwnershipKind Ownership { get; }
}