namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A concrete type definition that can have the full set of member kinds:
/// fields, indexers, constructors, operators, and cast operators in addition
/// to the methods and properties from <see cref="IMemberType"/>.
/// This applies to classes and structs.
/// </summary>
public interface IConcreteType : IMemberType
{
    /// <summary>
    /// The base type this concrete type inherits from (class → class, struct → struct).
    /// Null if no concrete base is declared.
    /// </summary>
    IMistType? BaseType { get; }

    /// <summary>
    /// Fields declared on this type.
    /// </summary>
    IReadOnlyList<IField> Fields { get; }

    /// <summary>
    /// Constructors declared on this type.
    /// </summary>
    IReadOnlyList<IConstructor> Constructors { get; }

    /// <summary>
    /// Operators declared on this type.
    /// </summary>
    IReadOnlyList<IOperator> Operators { get; }

    /// <summary>
    /// Cast operators declared on this type.
    /// </summary>
    IReadOnlyList<ICastOperator> CastOperators { get; }
}