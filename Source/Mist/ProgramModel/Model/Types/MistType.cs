using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Base class for all type definitions in the program model.
/// Contains the fields that 100% of types have regardless of kind.
/// </summary>
internal abstract class MistType : IMistType
{
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
    /// Indicates whether this type was declared in source code or projected from an assembly.
    /// Defaults to <see cref="TypeOrigin.Source"/> so existing source-declared types are unaffected.
    /// </summary>
    public TypeOrigin Origin { get; internal set; } = TypeOrigin.Source;

    IMistType? IMistType.DeclaringType => DeclaringType;

    IReadOnlyList<IMistType> IMistType.NestedTypes => NestedTypes;
}