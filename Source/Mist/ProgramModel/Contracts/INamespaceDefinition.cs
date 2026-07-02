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
    /// Type definitions declared within this namespace, keyed by name.
    /// Top-level types use their simple name (e.g., "MyClass").
    /// Nested types use a dot-qualified path from their outermost parent
    /// (e.g., "Outer.Inner").
    /// </summary>
    IReadOnlyDictionary<string, ITypeDefinition> Types { get; }
}