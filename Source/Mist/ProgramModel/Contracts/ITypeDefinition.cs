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
    /// The simple name of the type (e.g., "MyClass").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The fully qualified name including namespace (e.g., "MyNamespace.MyClass").
    /// </summary>
    string FullyQualifiedName { get; }

    /// <summary>
    /// The kind of type this definition represents.
    /// </summary>
    TypeDefinitionKind Kind { get; }

    /// <summary>
    /// The namespace this type belongs to, or null for types in the global namespace.
    /// </summary>
    INamespaceDefinition? ContainingNamespace { get; }

    /// <summary>
    /// The enclosing type definition, for nested types. Null for top-level types.
    /// </summary>
    ITypeDefinition? ContainingType { get; }

    /// <summary>
    /// The base type, if any (class inheritance or struct inheritance).
    /// </summary>
    ITypeReference? BaseType { get; }

    /// <summary>
    /// Abstract types (interfaces) this type implements or extends.
    /// </summary>
    IReadOnlyList<ITypeReference> ImplementedAbstracts { get; }

    /// <summary>
    /// Generic type parameters declared on this type.
    /// </summary>
    IReadOnlyList<ITypeParameterDefinition> TypeParameters { get; }

    /// <summary>
    /// Methods declared directly on this type (not inherited).
    /// </summary>
    IReadOnlyList<IMethodDefinition> Methods { get; }

    /// <summary>
    /// Constructors declared on this type.
    /// </summary>
    IReadOnlyList<IMethodDefinition> Constructors { get; }

    /// <summary>
    /// Properties declared directly on this type.
    /// </summary>
    IReadOnlyList<IPropertyDefinition> Properties { get; }

    /// <summary>
    /// Fields declared directly on this type.
    /// </summary>
    IReadOnlyList<IFieldDefinition> Fields { get; }

    /// <summary>
    /// Nested type definitions declared within this type.
    /// </summary>
    IReadOnlyList<ITypeDefinition> NestedTypes { get; }

    /// <summary>
    /// Modifiers applied to this type (readonly, owned, intrinsic, etc.).
    /// </summary>
    TypeModifiers Modifiers { get; }

    /// <summary>
    /// The source location where this type was declared.
    /// </summary>
    SourceOrigin? Origin { get; }
}