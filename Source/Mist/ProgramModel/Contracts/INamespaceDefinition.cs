namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A namespace in the program model. Contains type definitions and nested namespaces.
/// </summary>
public interface INamespaceDefinition
{
    /// <summary>
    /// The simple name of this namespace (e.g., "Collections" in "System.Collections").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The fully qualified name (e.g., "System.Collections.Generic").
    /// </summary>
    string FullyQualifiedName { get; }

    /// <summary>
    /// The parent namespace, or null for root-level namespaces.
    /// </summary>
    INamespaceDefinition? Parent { get; }

    /// <summary>
    /// Namespaces nested directly within this namespace.
    /// </summary>
    IReadOnlyList<INamespaceDefinition> ChildNamespaces { get; }

    /// <summary>
    /// Type definitions declared directly within this namespace (not nested types).
    /// </summary>
    IReadOnlyList<ITypeDefinition> Types { get; }
}