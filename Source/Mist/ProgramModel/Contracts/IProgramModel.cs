namespace Mist.ProgramModel.Contracts;

/// <summary>
/// The fully resolved semantic representation of a Mist program.
/// Contains all declared types, namespaces, and their relationships —
/// navigable without reference to syntax trees or the legacy semantic model.
/// </summary>
public interface IProgramModel
{
    /// <summary>
    /// All top-level namespaces declared in the program.
    /// </summary>
    IReadOnlyList<INamespaceDefinition> Namespaces { get; }

    /// <summary>
    /// All type definitions declared in the program (classes, structs, abstracts, enums,
    /// newtypes, type aliases), flattened across namespaces for convenient iteration.
    /// </summary>
    IReadOnlyList<ITypeDefinition> Types { get; }

    /// <summary>
    /// All extension blocks declared in the program, indexed by receiver type.
    /// </summary>
    IReadOnlyList<IExtensionDefinition> Extensions { get; }

    /// <summary>
    /// With-block conformance registrations mapping concrete types to abstract
    /// type obligations they satisfy.
    /// </summary>
    IReadOnlyList<IConformanceDeclaration> Conformances { get; }

    /// <summary>
    /// Looks up a type definition by its fully qualified name (e.g., "MyNamespace.MyClass").
    /// Returns null if no type with that name exists.
    /// </summary>
    ITypeDefinition? FindType(string fullyQualifiedName);

    /// <summary>
    /// Looks up a namespace definition by its fully qualified name.
    /// Returns null if no namespace with that name exists.
    /// </summary>
    INamespaceDefinition? FindNamespace(string fullyQualifiedName);
}