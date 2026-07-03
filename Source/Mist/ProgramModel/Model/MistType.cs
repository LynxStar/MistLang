using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Base class for all type definitions in the program model.
/// Contains the fields that 100% of types have regardless of kind.
/// </summary>
internal abstract class MistType : IMistType
{
    protected MistType(
        string name,
        string namespaceName,
        Scope scope,
        MistType? declaringType)
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

    public MistType? DeclaringType { get; }

    public List<MistType> NestedTypes { get; } = [];

    IMistType? IMistType.DeclaringType => DeclaringType;

    IReadOnlyList<IMistType> IMistType.NestedTypes => NestedTypes;
}