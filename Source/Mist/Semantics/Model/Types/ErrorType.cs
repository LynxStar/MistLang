using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Sentinel type for unresolved or erroneous types. Has no TypeKind.
/// Used only during analysis; must not propagate to the backend.
/// </summary>
/// <remarks>
/// Phase B of the `unknown-type` spec will reintroduce <c>UnknownType</c> as the
/// user-facing top type, distinct from this <c>ErrorType</c> sentinel. Do not
/// conflate the two: <c>ErrorType</c> has no <see cref="TypeKind"/>, no display
/// spelling in source (<c>"?"</c> is diagnostic only), and never reaches the
/// backend; the future <c>UnknownType</c> is spelled <c>unknown</c>, has its
/// own <see cref="TypeKind"/>, and lowers to C# <c>object</c>.
/// </remarks>
public sealed class ErrorType : IType
{
    public static readonly ErrorType Instance = new();
    public string DisplayName => "?";
    public TypeKind Kind => throw new InvalidOperationException(
        "ErrorType has no TypeKind — it is an error sentinel.");

    private ErrorType() { }
}
