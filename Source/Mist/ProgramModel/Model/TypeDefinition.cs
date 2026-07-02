using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Base class for all type definitions in the program model.
/// Contains the fields that 100% of types have regardless of kind.
/// </summary>
internal abstract class TypeDefinition : ITypeDefinition
{
    protected TypeDefinition(
        string name,
        string namespaceName,
        Scope scope,
        TypeDefinition? declaringType)
    {
        Name = name;
        NamespaceName = namespaceName;
        Scope = scope;
        DeclaringType = declaringType;
    }

    public string Name { get; }

    public string NamespaceName { get; }

    /// <summary>
    /// The type's own scope. Parent is the file scope for top-level types,
    /// or the enclosing type's scope for nested types.
    /// </summary>
    public Scope Scope { get; }

    public TypeDefinition? DeclaringType { get; }

    public List<TypeDefinition> NestedTypes { get; } = [];

    ITypeDefinition? ITypeDefinition.DeclaringType => DeclaringType;

    IReadOnlyList<ITypeDefinition> ITypeDefinition.NestedTypes => NestedTypes;
}