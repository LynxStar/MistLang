namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A concrete type definition that can have the full set of member kinds:
/// fields, indexers, constructors, operators, and cast operators in addition
/// to the methods and properties from <see cref="IMemberTypeDefinition"/>.
/// This applies to classes and structs.
/// </summary>
public interface IConcreteTypeDefinition : IMemberTypeDefinition
{
    /// <summary>
    /// The base type this concrete type inherits from (class → class, struct → struct).
    /// Null if no concrete base is declared.
    /// </summary>
    ITypeDefinition? BaseType { get; }

    /// <summary>
    /// Fields declared on this type.
    /// </summary>
    IReadOnlyList<IFieldDefinition> Fields { get; }

    /// <summary>
    /// Indexers declared on this type.
    /// </summary>
    IReadOnlyList<IIndexerDefinition> Indexers { get; }

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