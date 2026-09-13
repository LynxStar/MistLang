using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

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
        Scope? parentScope,
        MistType? declaringType,
        TypeDeclarationNode? declarationNode)
        : base(name, namespaceName, parentScope, declaringType, declarationNode)
    {
    }

    /// <summary>
    /// Whether this type is sealed (cannot be inherited from).
    /// </summary>
    public bool IsSealed => Modifiers.IsSealed;

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
    /// Whether this type has a Drop() method (explicit or synthesized) or implements IDisposable.
    /// </summary>
    public bool HasDrop { get; internal set; }

    /// <summary>
    /// Whether this type's drop semantics come from IDisposable rather than an explicit Drop() method.
    /// When true, the emitter emits .Dispose() instead of .Drop() at drop emission sites.
    /// </summary>
    public bool IsDisposableDrop { get; internal set; }

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

    /// <summary>
    /// Deliberately NOT folded into <see cref="MemberType.BaseAbstractTypes"/>: that would let member
    /// lookup resolve a slot answered only by an explicit interface implementation, which C# does not
    /// admit on a concrete-typed receiver.
    /// </summary>
    public List<ConformanceDeclaration> ConformancesList { get; } = [];

    /// <summary>
    /// Set on a newtype's renamed clone (newtypes spec Requirement 3.2): the class or struct whose
    /// declaration this type was bound from, which is never this type's base -- a clone has no nominal
    /// relationship to its source.
    /// </summary>
    public ConcreteType? CloneSource { get; internal set; }

    public NewtypeDeclarationNode? NewtypeDeclaration { get; internal set; }

    /// <summary>
    /// Adds the delegates only classes and structs can declare — constructors and
    /// cast operators — to the methods and operators every member type has.
    /// </summary>
    internal override IEnumerable<Method> EnumerateDelegates()
    {
        foreach(var inherited in base.EnumerateDelegates())
        {
            yield return inherited;
        }

        foreach(var constructor in ConstructorsList)
        {
            yield return constructor;
        }

        foreach(var castOperator in CastOperatorsList)
        {
            yield return castOperator;
        }
    }

    IMistType? IConcreteType.BaseType => BaseType;

    IReadOnlyList<IField> IConcreteType.Fields => FieldsList;

    IReadOnlyList<IConstructor> IConcreteType.Constructors => ConstructorsList;

    IReadOnlyList<ICastOperator> IConcreteType.CastOperators => CastOperatorsList;
}