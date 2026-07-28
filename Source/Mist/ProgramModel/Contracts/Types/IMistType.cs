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
    /// The fully qualified name of this type, including any namespace and nesting path. For
    /// example: "Standard.Int32", "MyApp.Outer.Inner". Used as the primary key in the flat type
    /// registry.
    /// <para>
    /// Every type has one, but only a declared type is qualified by a NAMESPACE -- a tuple or a
    /// union has no namespace to be reachable through, and reports its structural name here. The
    /// namespace itself lives on the declared types alone.
    /// </para>
    /// </summary>
    string FullyQualifiedName { get; }

    /// <summary>
    /// The enclosing type for nested types; null for top-level types.
    /// </summary>
    IMistType? DeclaringType { get; }

    /// <summary>
    /// Nested types declared within this type.
    /// </summary>
    IReadOnlyList<IMistType> NestedTypes { get; }

    /// <summary>
    /// The assembly this type belongs to, and the single source of truth for where the
    /// type came from. Never null: compiler-constructed types report
    /// <see cref="MistAssemblyKind.Synthetic"/> rather than nothing at all.
    /// </summary>
    IAssembly Assembly { get; }
}