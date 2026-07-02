using Mist.Generated;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Base for concrete types (classes and structs) that can have the full set
/// of member kinds: fields, indexers, constructors, operators, and cast operators.
/// </summary>
internal abstract class ConcreteTypeDefinition : MemberTypeDefinition, IConcreteTypeDefinition
{
    protected ConcreteTypeDefinition(
        string name,
        TypeDefinitionKind kind,
        string namespaceName,
        Scope scope,
        TypeDefinition? declaringType,
        TypeDeclarationNode declarationNode)
        : base(name, kind, namespaceName, scope, declaringType, declarationNode)
    {
    }

    public List<FieldDefinition> FieldsList { get; } = [];

    public List<IndexerDefinition> IndexersList { get; } = [];

    public List<ConstructorDefinition> ConstructorsList { get; } = [];

    public List<OperatorDefinition> OperatorsList { get; } = [];

    public List<CastOperatorDefinition> CastOperatorsList { get; } = [];

    IReadOnlyList<IFieldDefinition> IConcreteTypeDefinition.Fields => FieldsList;

    IReadOnlyList<IIndexerDefinition> IConcreteTypeDefinition.Indexers => IndexersList;

    IReadOnlyList<IConstructorDefinition> IConcreteTypeDefinition.Constructors => ConstructorsList;

    IReadOnlyList<IOperatorDefinition> IConcreteTypeDefinition.Operators => OperatorsList;

    IReadOnlyList<ICastOperatorDefinition> IConcreteTypeDefinition.CastOperators => CastOperatorsList;
}