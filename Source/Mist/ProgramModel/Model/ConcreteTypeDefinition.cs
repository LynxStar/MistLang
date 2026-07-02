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
        string namespaceName,
        Scope scope,
        TypeDefinition? declaringType,
        TypeDeclarationNode declarationNode)
        : base(name, namespaceName, scope, declaringType, declarationNode)
    {
    }

    /// <summary>
    /// The base type this concrete type inherits from (class → class, struct → struct).
    /// Null if no concrete base is declared.
    /// </summary>
    public TypeDefinition? BaseType { get; internal set; }

    /// <summary>
    /// Whether this type is backend-backed (intrinsic keyword).
    /// </summary>
    public bool IsIntrinsic { get; internal set; }

    /// <summary>
    /// Whether this type has owned semantics (owned keyword).
    /// </summary>
    public bool IsOwned { get; internal set; }

    /// <summary>
    /// The substructural discipline (affine, linear, relevant, or none).
    /// </summary>
    public SubstructuralDiscipline Discipline { get; internal set; }

    public List<FieldDefinition> FieldsList { get; } = [];

    public List<IndexerDefinition> IndexersList { get; } = [];

    public List<ConstructorDefinition> ConstructorsList { get; } = [];

    public List<OperatorDefinition> OperatorsList { get; } = [];

    public List<CastOperatorDefinition> CastOperatorsList { get; } = [];

    ITypeDefinition? IConcreteTypeDefinition.BaseType => BaseType;

    IReadOnlyList<IFieldDefinition> IConcreteTypeDefinition.Fields => FieldsList;

    IReadOnlyList<IIndexerDefinition> IConcreteTypeDefinition.Indexers => IndexersList;

    IReadOnlyList<IConstructorDefinition> IConcreteTypeDefinition.Constructors => ConstructorsList;

    IReadOnlyList<IOperatorDefinition> IConcreteTypeDefinition.Operators => OperatorsList;

    IReadOnlyList<ICastOperatorDefinition> IConcreteTypeDefinition.CastOperators => CastOperatorsList;
}