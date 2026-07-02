using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A user-declared type in the program model. Provides a unified view over
/// classes, structs, abstracts, enums, newtypes, and type aliases — navigable
/// through its members without reference to syntax.
/// </summary>
public interface ITypeDefinition
{
    /// <summary>
    /// The simple (unqualified) name of the type.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The kind of type (class, struct, abstract, enum, newtype, type alias).
    /// </summary>
    TypeDefinitionKind Kind { get; }

    /// <summary>
    /// The fully qualified namespace this type belongs to.
    /// </summary>
    string NamespaceName { get; }

    /// <summary>
    /// The enclosing type for nested types; null for top-level types.
    /// </summary>
    ITypeDefinition? DeclaringType { get; }

    /// <summary>
    /// Nested types declared within this type.
    /// </summary>
    IReadOnlyList<ITypeDefinition> NestedTypes { get; }
}