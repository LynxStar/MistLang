namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A namespace in the program model. Contains type definitions and nested namespaces.
/// </summary>
public interface INamespace
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
    IReadOnlyDictionary<string, IMistType> Types { get; }
}