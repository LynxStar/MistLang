using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Base of the types that inherit and declare members: classes, structs and abstracts.
/// </summary>
internal abstract class MemberType : DeclaredType, IMemberType, IHasTypeParameters, IMethodHost, IHasAttributes
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
    /// Kept from Phase 1 so Phase 2 reaches member declarations without re-walking. Null for an
    /// assembly-projected type.
    /// </summary>
    internal TypeDeclarationNode? DeclarationNode { get; }

    public List<TypeParameter> TypeParametersList { get; } = [];

    public MemberModifiers Modifiers { get; internal set; }

    public bool IsReadonly => Modifiers.IsReadonly;

    public List<AttributeData> AttributesList { get; } = [];

    public List<DeclaredAttribute> DeclaredAttributesList { get; } = [];

    public List<MistType> BaseAbstractTypes { get; } = [];

    public List<Method> MethodsList { get; } = [];

    public List<Property> PropertiesList { get; } = [];

    public List<Indexer> IndexersList { get; } = [];

    // CLR declarations can reserve names even when their members cannot be projected into Mist.
    public HashSet<string> UnprojectedMemberNames { get; } = new(StringComparer.Ordinal);

    public List<WithholdingFrame> WithheldMembers { get; } = [];

    public List<Operator> OperatorsList { get; } = [];

    /// <summary>
    /// Constructors, operators and cast operators derive from <see cref="Method"/> but are kept in
    /// separate lists because type kinds admit different subsets of them; this walk restores the single
    /// view the grammar's <c>Delegate</c> trait describes. Includes bodiless delegates.
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

    IReadOnlyList<ITypeParameter> IGenericDeclaration.TypeParameters => TypeParametersList;

    IReadOnlyList<IMistType> IMemberType.BaseAbstractTypes => BaseAbstractTypes;

    IReadOnlyList<IMethod> IMemberType.Methods => MethodsList;

    IReadOnlyList<IProperty> IMemberType.Properties => PropertiesList;

    IReadOnlyList<IIndexer> IMemberType.Indexers => IndexersList;

    IReadOnlyList<IOperator> IMemberType.Operators => OperatorsList;

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;

    bool IMethodHost.IsAbstract => this is AbstractType;

    bool IMethodHost.IsIntrinsic => this is ConcreteType concrete && concrete.IsIntrinsic;
}