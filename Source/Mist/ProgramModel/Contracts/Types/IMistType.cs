namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A user-declared type in the program model. Provides a unified view over
/// classes, structs, abstracts, enums, newtypes, and type aliases — navigable
/// through its members without reference to syntax.
/// </summary>
public interface IMistType
{
    /// <summary>
    /// The simple (unqualified) name of the type.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The fully qualified namespace this type belongs to.
    /// </summary>
    string NamespaceName { get; }

    /// <summary>
    /// The enclosing type for nested types; null for top-level types.
    /// </summary>
    IMistType? DeclaringType { get; }

    /// <summary>
    /// Nested types declared within this type.
    /// </summary>
    IReadOnlyList<IMistType> NestedTypes { get; }

    /// <summary>
    /// Indicates whether this type was declared in source code or projected from an assembly.
    /// </summary>
    TypeOrigin Origin { get; }
}