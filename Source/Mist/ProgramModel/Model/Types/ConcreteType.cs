using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Base for concrete types (classes and structs) that can have the full set
/// of member kinds: fields, indexers, constructors, operators, and cast operators.
/// </summary>
internal abstract class ConcreteType : MemberType, IConcreteType
{
    protected ConcreteType(
        string name,
        string namespaceName,
        Scope scope,
        MistType? declaringType,
        TypeDeclarationNode declarationNode)
        : base(name, namespaceName, scope, declaringType, declarationNode)
    {
    }

    /// <summary>
    /// The base type this concrete type inherits from (class → class, struct → struct).
    /// Null if no concrete base is declared.
    /// </summary>
    public MistType? BaseType { get; internal set; }

    /// <summary>
    /// Whether this type is backend-backed (intrinsic keyword).
    /// </summary>
    public bool IsIntrinsic => Modifiers.IsIntrinsic;

    /// <summary>
    /// Whether this type has owned semantics (owned keyword).
    /// </summary>
    public bool IsOwned => Modifiers.IsOwned;

    /// <summary>
    /// The substructural discipline (affine, linear, relevant, or none).
    /// </summary>
    public SubstructuralDiscipline Discipline => Modifiers.Discipline;

    public List<Field> FieldsList { get; } = [];

    public List<Constructor> ConstructorsList { get; } = [];

    public List<CastOperator> CastOperatorsList { get; } = [];

    /// <summary>
    /// The explicit default block declared on this type, or null if none was declared.
    /// </summary>
    public DefaultBlock? DefaultBlock { get; internal set; }

    IMistType? IConcreteType.BaseType => BaseType;

    IReadOnlyList<IField> IConcreteType.Fields => FieldsList;

    IReadOnlyList<IConstructor> IConcreteType.Constructors => ConstructorsList;

    IReadOnlyList<ICastOperator> IConcreteType.CastOperators => CastOperatorsList;
}