using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// The fully resolved semantic representation of a Mist program.
/// Contains all declared types, namespaces, and their relationships --
/// navigable without reference to syntax trees or the legacy semantic model.
/// </summary>
public interface IProgramModel
{
    /// <summary>
    /// All types in the program, keyed by fully qualified name.
    /// This is the primary type registry — all name resolution ultimately
    /// resolves to keys in this dictionary.
    /// </summary>
    IReadOnlyDictionary<string, IMistType> TypesByFQN { get; }

    /// <summary>
    /// All retroactive conformance declarations in the program.
    /// Each declares that a type conforms to an abstract via member mappings.
    /// Scoped by their declaring namespace -- only active when that namespace is imported.
    /// </summary>
    IReadOnlyList<IConformanceDeclaration> Conformances { get; }
}