namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A namespace in the program model. Holds namespace-scoped declarations
/// (extensions, conformances) that are only active when the namespace is imported.
/// Types are stored in the flat <see cref="IProgramModel.TypesByFQN"/> dictionary.
/// </summary>
public interface INamespace
{
    /// <summary>
    /// The fully qualified name (e.g., "System.Collections.Generic").
    /// </summary>
    string FullyQualifiedName { get; }

    /// <summary>
    /// Extension blocks declared within this namespace, grouped by receiver type.
    /// Multiple extension blocks may target the same receiver type; each retains
    /// its own scope and declarations.
    /// </summary>
    IReadOnlyDictionary<IMistType, IReadOnlyList<IExtensionBlock>> Extensions { get; }
}