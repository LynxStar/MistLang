using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Base class for all type definitions in the program model.
/// Contains the fields that 100% of types have regardless of kind.
/// </summary>
internal abstract class MistType : IMistType
{
    /// <summary>
    /// A type that borrows its scope and whose qualified name is just its nesting path: the
    /// structural types, which have no namespace to be qualified by, and a type parameter, which is
    /// named through the declaration that introduced it.
    /// </summary>
    protected MistType(string name, Scope scope, MistType? declaringType)
        : this(name, Nest(name, declaringType), scope, declaringType)
    {
    }

    /// <summary>
    /// A type that borrows its scope but has a qualified name of its own: a constructed generic,
    /// qualified by its definition, and the emitter's sentinel markers, which are matched by fully
    /// qualified name without ever being registered under one.
    /// </summary>
    protected MistType(string name, string fullyQualifiedName, Scope scope, MistType? declaringType)
    {
        Name = name;
        FullyQualifiedName = fullyQualifiedName;
        Scope = scope;
        DeclaringType = declaringType;
    }

    /// <summary>
    /// A type that OWNS its scope -- see <see cref="DeclaredType"/>. The scope is built here rather
    /// than passed in because it needs this type, and this type needs it: handing the factory
    /// <c>this</c> is what lets both ends be readonly instead of one being filled in after
    /// construction.
    /// </summary>
    /// <param name="scopeFactory">
    /// Receives this type and returns the scope it owns. Called with a partially constructed type,
    /// so it must store the reference and read nothing from it.
    /// </param>
    protected MistType(
        string name,
        string fullyQualifiedName,
        MistType? declaringType,
        Func<MistType, Scope> scopeFactory)
    {
        Name = name;
        FullyQualifiedName = fullyQualifiedName;
        DeclaringType = declaringType;
        Scope = scopeFactory(this);
    }

    public string Name { get; }

    /// <inheritdoc />
    public string FullyQualifiedName { get; }

    /// <summary>
    /// A name qualified by its nesting path alone, which is as much as this base can know. A
    /// namespace prefix is <see cref="DeclaredType"/>'s to add, since only a declared type has one.
    /// </summary>
    protected static string Nest(string name, MistType? declaringType)
    {
        return declaringType is null
            ? name
            : $"{declaringType.FullyQualifiedName}.{name}"
            ;
    }

    /// <summary>
    /// The type's own scope. Parent is the file scope for top-level types,
    /// or the enclosing type's scope for nested types.
    /// </summary>
    public Scope Scope { get; }

    public MistType? DeclaringType { get; }

    public List<MistType> NestedTypes { get; } = [];

    /// <summary>
    /// The assembly this type belongs to, overwritten when the type is registered
    /// against a declaration boundary.
    /// <para>
    /// Defaults to <see cref="MistAssembly.Synthetic"/>, which is the correct answer for
    /// every type the compiler constructs rather than reads from a declaration, and those
    /// are exactly the types that are never registered. Registration is therefore the
    /// only thing that changes it, and the property is never null.
    /// </para>
    /// </summary>
    public MistAssembly Assembly { get; internal set; } = MistAssembly.Synthetic;

    IMistType? IMistType.DeclaringType => DeclaringType;

    IReadOnlyList<IMistType> IMistType.NestedTypes => NestedTypes;

    IAssembly IMistType.Assembly => Assembly;
}