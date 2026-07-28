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
    /// For a type that BORROWS its scope: a type parameter, which lives in the scope of whatever
    /// declared it, and the structural types, which have nothing declared inside them.
    /// </summary>
    protected MistType(
        string name,
        string namespaceName,
        Scope scope,
        MistType? declaringType)
    {
        Name = name;
        NamespaceName = namespaceName;
        Scope = scope;
        DeclaringType = declaringType;
        FullyQualifiedName = BuildFQN(name, namespaceName, declaringType);
    }

    /// <summary>
    /// For a type that OWNS its scope -- see <see cref="DeclaredType"/>. The scope is built here
    /// rather than passed in because it needs this type, and this type needs it: handing the
    /// factory <c>this</c> is what lets both ends be readonly instead of one being filled in after
    /// construction.
    /// </summary>
    /// <param name="scopeFactory">
    /// Receives this type and returns the scope it owns. Called with a partially constructed type,
    /// so it must store the reference and read nothing from it.
    /// </param>
    protected MistType(
        string name,
        string namespaceName,
        MistType? declaringType,
        Func<MistType, Scope> scopeFactory)
    {
        Name = name;
        NamespaceName = namespaceName;
        DeclaringType = declaringType;
        FullyQualifiedName = BuildFQN(name, namespaceName, declaringType);
        Scope = scopeFactory(this);
    }

    public string Name { get; }

    public string NamespaceName { get; }

    /// <inheritdoc />
    public string FullyQualifiedName { get; }

    /// <summary>
    /// Computes the FQN for a type: "Namespace.Name" for top-level types,
    /// "Namespace.Outer.Inner" for nested types (walks the declaring chain).
    /// </summary>
    private static string BuildFQN(string name, string namespaceName, MistType? declaringType)
    {
        if(declaringType is not null)
        {
            return $"{declaringType.FullyQualifiedName}.{name}";
        }

        if(string.IsNullOrEmpty(namespaceName))
        {
            return name;
        }

        return $"{namespaceName}.{name}";
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