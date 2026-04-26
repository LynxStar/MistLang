using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Sentinel type for unresolved or erroneous types. Has no TypeKind.
/// Used only during analysis; must not propagate to the backend.
/// </summary>
public sealed class UnknownType : IType
{
    public static readonly UnknownType Instance = new();
    public string DisplayName => "?";
    public TypeKind Kind => throw new InvalidOperationException(
        "UnknownType has no TypeKind — it is an error sentinel.");

    private UnknownType() { }
}
