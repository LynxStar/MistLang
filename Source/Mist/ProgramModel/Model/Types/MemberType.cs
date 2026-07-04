using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Intermediate base for types that participate in inheritance and have
/// method/property declarations: classes, structs, and abstracts.
/// </summary>
internal abstract class MemberType : MistType, IMemberType
{
    protected MemberType(
        string name,
        string namespaceName,
        Scope scope,
        MistType? declaringType,
        TypeDeclarationNode declarationNode)
        : base(name, namespaceName, scope, declaringType)
    {
        DeclarationNode = declarationNode;
    }

    /// <summary>
    /// The AST declaration node for this type. Stashed during Phase 1
    /// so Phase 2 can access member declarations without re-walking.
    /// </summary>
    internal TypeDeclarationNode DeclarationNode { get; }

    /// <summary>
    /// The modifiers declared on this type.
    /// </summary>
    public MemberModifiers Modifiers { get; internal set; }

    /// <summary>
    /// Whether this type is declared as readonly.
    /// </summary>
    public bool IsReadonly => Modifiers.IsReadonly;

    /// <summary>
    /// Abstracts that this type implements or refines.
    /// </summary>
    public List<MistType> BaseAbstractTypes { get; } = [];

    public List<Method> MethodsList { get; } = [];

    public List<Property> PropertiesList { get; } = [];

    public List<Indexer> IndexersList { get; } = [];

    public List<Operator> OperatorsList { get; } = [];

    IReadOnlyList<IMistType> IMemberType.BaseAbstractTypes => BaseAbstractTypes;

    IReadOnlyList<IMethod> IMemberType.Methods => MethodsList;

    IReadOnlyList<IProperty> IMemberType.Properties => PropertiesList;

    IReadOnlyList<IIndexer> IMemberType.Indexers => IndexersList;

    IReadOnlyList<IOperator> IMemberType.Operators => OperatorsList;
}