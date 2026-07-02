using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Base class for all type definitions in the program model.
/// Contains the fields that 100% of types have regardless of kind.
/// </summary>
internal abstract class TypeDefinition : ITypeDefinition
{
    protected TypeDefinition(
        string name,
        TypeDefinitionKind kind,
        string namespaceName,
        Scope scope,
        TypeDefinition? declaringType)
    {
        Name = name;
        Kind = kind;
        NamespaceName = namespaceName;
        Scope = scope;
        DeclaringType = declaringType;
    }

    public string Name { get; }

    public TypeDefinitionKind Kind { get; }

    public string NamespaceName { get; }

    /// <summary>
    /// The type's own scope. Parent is the file scope for top-level types,
    /// or the enclosing type's scope for nested types.
    /// </summary>
    public Scope Scope { get; }

    public TypeDefinition? DeclaringType { get; }

    /// <summary>
    /// The AST declaration node for this type. Stashed during Phase 1
    /// so Phase 2 can access member declarations without re-walking.
    /// </summary>
    internal Storm.Runtime.SyntaxNode? DeclarationNode { get; set; }

    public List<TypeDefinition> NestedTypes { get; } = [];

    public List<ITypeReference> BaseTypes { get; } = [];

    public List<FieldDefinition> FieldsList { get; } = [];

    public List<PropertyDefinition> PropertiesList { get; } = [];

    public List<IndexerDefinition> IndexersList { get; } = [];

    public List<MethodDefinition> MethodsList { get; } = [];

    public List<ConstructorDefinition> ConstructorsList { get; } = [];

    public List<OperatorDefinition> OperatorsList { get; } = [];

    public List<CastOperatorDefinition> CastOperatorsList { get; } = [];

    ITypeDefinition? ITypeDefinition.DeclaringType => DeclaringType;

    IReadOnlyList<ITypeDefinition> ITypeDefinition.NestedTypes => NestedTypes;

    IReadOnlyList<ITypeReference> ITypeDefinition.BaseTypes => BaseTypes;

    IReadOnlyList<IFieldDefinition> ITypeDefinition.Fields => FieldsList;

    IReadOnlyList<IPropertyDefinition> ITypeDefinition.Properties => PropertiesList;

    IReadOnlyList<IIndexerDefinition> ITypeDefinition.Indexers => IndexersList;

    IReadOnlyList<IMethodDefinition> ITypeDefinition.Methods => MethodsList;

    IReadOnlyList<IConstructorDefinition> ITypeDefinition.Constructors => ConstructorsList;

    IReadOnlyList<IOperatorDefinition> ITypeDefinition.Operators => OperatorsList;

    IReadOnlyList<ICastOperatorDefinition> ITypeDefinition.CastOperators => CastOperatorsList;
}