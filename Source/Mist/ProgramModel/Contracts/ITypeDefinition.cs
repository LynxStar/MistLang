using Mist.ProgramModel.Contracts.TypeReferences;
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

    /// <summary>
    /// Base types and implemented abstracts.
    /// </summary>
    IReadOnlyList<ITypeReference> BaseTypes { get; }

    /// <summary>
    /// Fields declared on this type.
    /// </summary>
    IReadOnlyList<IFieldDefinition> Fields { get; }

    /// <summary>
    /// Properties declared on this type.
    /// </summary>
    IReadOnlyList<IPropertyDefinition> Properties { get; }

    /// <summary>
    /// Indexers declared on this type.
    /// </summary>
    IReadOnlyList<IIndexerDefinition> Indexers { get; }

    /// <summary>
    /// Methods declared on this type.
    /// </summary>
    IReadOnlyList<IMethodDefinition> Methods { get; }

    /// <summary>
    /// Constructors declared on this type.
    /// </summary>
    IReadOnlyList<IConstructorDefinition> Constructors { get; }

    /// <summary>
    /// Operators declared on this type.
    /// </summary>
    IReadOnlyList<IOperatorDefinition> Operators { get; }

    /// <summary>
    /// Cast operators declared on this type.
    /// </summary>
    IReadOnlyList<ICastOperatorDefinition> CastOperators { get; }
}