namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A namespace in the program model. Contains type definitions and nested namespaces.
/// </summary>
public interface INamespaceDefinition
{
    /// <summary>
    /// The fully qualified name (e.g., "System.Collections.Generic").
    /// </summary>
    string FullyQualifiedName { get; }

    /// <summary>
    /// Type definitions declared directly within this namespace, keyed by simple name.
    /// </summary>
    IReadOnlyDictionary<string, ITypeDefinition> Types { get; }
}