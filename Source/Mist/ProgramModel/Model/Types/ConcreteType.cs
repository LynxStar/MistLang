using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Base of classes and structs.
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

    public bool IsSealed => Modifiers.IsSealed;

    /// <summary>
    /// A class's base is a class and a struct's a struct.
    /// </summary>
    public MistType? BaseType { get; internal set; }

    /// <summary>
    /// Recorded because <see cref="BaseType"/> is null both when no base was written and when the one
    /// written failed to resolve, and only the second can hide inherited members no rule can see.
    /// </summary>
    public bool BaseUnresolved { get; internal set; }

    public bool IsIntrinsic => Modifiers.IsIntrinsic;

    /// <summary>
    /// An explicit or synthesized Drop(), or IDisposable.
    /// </summary>
    public bool HasDrop { get; internal set; }

    /// <summary>
    /// Drop comes from IDisposable, so the emitter calls .Dispose() rather than .Drop().
    /// </summary>
    public bool IsDisposableDrop { get; internal set; }

    public bool IsOwned => Modifiers.IsOwned;

    public SubstructuralDiscipline Discipline => Modifiers.Discipline;

    public List<Field> FieldsList { get; } = [];

    public StorageLayout? ImportedStorage { get; internal set; }

    public List<Constructor> ConstructorsList { get; } = [];

    public List<CastOperator> CastOperatorsList { get; } = [];

    internal CastOperator? ErrorAdaptation { get; set; }

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

    public IMistType? CloneSourceType { get; internal set; }

    public UnheldCopy? UnheldCopy { get; internal set; }

    public NewtypeDeclarationNode? NewtypeDeclaration { get; internal set; }

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