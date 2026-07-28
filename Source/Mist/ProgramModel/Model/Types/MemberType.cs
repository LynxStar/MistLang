using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Intermediate base for types that participate in inheritance and have
/// method/property declarations: classes, structs, and abstracts.
/// </summary>
internal abstract class MemberType : DeclaredType, IMemberType, IMethodHost, IHasAttributes
{
    protected MemberType(
        string name,
        string namespaceName,
        Scope? parentScope,
        MistType? declaringType,
        TypeDeclarationNode? declarationNode)
        : base(name, namespaceName, parentScope, declaringType)
    {
        DeclarationNode = declarationNode;
    }

    /// <summary>
    /// The AST declaration node for this type. Stashed during Phase 1
    /// so Phase 2 can access member declarations without re-walking.
    /// Null for assembly-projected types (which have no source AST).
    /// </summary>
    internal TypeDeclarationNode? DeclarationNode { get; }

    /// <summary>
    /// The type parameters declared on this type. Empty for non-generic types.
    /// Populated during Phase 1 when the type is registered.
    /// </summary>
    public List<TypeParameter> TypeParametersList { get; } = [];

    /// <summary>
    /// The modifiers declared on this type.
    /// </summary>
    public MemberModifiers Modifiers { get; internal set; }

    /// <summary>
    /// Whether this type is declared as readonly.
    /// </summary>
    public bool IsReadonly => Modifiers.IsReadonly;

    /// <summary>
    /// The attributes applied to this type.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    /// <summary>
    /// Abstracts that this type implements or refines.
    /// </summary>
    public List<MistType> BaseAbstractTypes { get; } = [];

    public List<Method> MethodsList { get; } = [];

    public List<Property> PropertiesList { get; } = [];

    public List<Indexer> IndexersList { get; } = [];

    public List<Operator> OperatorsList { get; } = [];

    /// <summary>
    /// Every delegate declared on this type, regardless of which list it is stored in.
    /// Constructors, operators, and cast operators all derive from <see cref="Method"/>,
    /// but are kept in separate lists because different type kinds admit different
    /// subsets of them. This walk restores the single view of them that the grammar's
    /// <c>Delegate</c> trait describes, so consumers that treat all delegates alike
    /// (body binding, emission) do not have to enumerate each list by hand.
    /// Includes bodiless delegates: filter on <see cref="Method.Body"/> if that matters.
    /// </summary>
    internal virtual IEnumerable<Method> EnumerateDelegates()
    {
        foreach(var method in MethodsList)
        {
            yield return method;
        }

        foreach(var op in OperatorsList)
        {
            yield return op;
        }
    }

    IReadOnlyList<ITypeParameter> IMemberType.TypeParameters => TypeParametersList;

    IReadOnlyList<IMistType> IMemberType.BaseAbstractTypes => BaseAbstractTypes;

    IReadOnlyList<IMethod> IMemberType.Methods => MethodsList;

    IReadOnlyList<IProperty> IMemberType.Properties => PropertiesList;

    IReadOnlyList<IIndexer> IMemberType.Indexers => IndexersList;

    IReadOnlyList<IOperator> IMemberType.Operators => OperatorsList;

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;

    bool IMethodHost.IsAbstract => this is AbstractType;

    bool IMethodHost.IsIntrinsic => this is ConcreteType concrete && concrete.IsIntrinsic;
}