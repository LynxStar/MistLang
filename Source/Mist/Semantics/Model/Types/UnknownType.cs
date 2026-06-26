using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// The user-facing <c>unknown</c> top type (spelled <c>unknown</c> in Mist source).
/// </summary>
/// <remarks>
/// <para>
/// Every non-<c>void</c> type is implicitly assignable to <see cref="UnknownType"/>,
/// but <c>unknown</c> is forbidden as the declared type of writable storage (fields,
/// properties with backing fields, indexer elements, <c>new</c> expressions, and
/// generic-type-argument positions that land at storage). Pattern matching
/// (<c>is T t</c>) is the designated mechanism for narrowing an <c>unknown</c>-typed
/// binding back to a concrete type.
/// </para>
/// <para>
/// Distinct from <see cref="ErrorType"/>: <c>ErrorType</c> is the internal
/// type-resolution error sentinel (display name <c>"?"</c>, no <see cref="TypeKind"/>,
/// never reaches the backend); <c>UnknownType</c> is a real user-facing type with
/// <see cref="TypeKind.Unknown"/>, display name <c>"unknown"</c>, and lowers to C#
/// <c>object</c> at emission time.
/// </para>
/// <para>
/// <c>UnknownType</c> has no declared members — no fields, methods, properties,
/// constructors, or operators — and does not participate in inheritance as either
/// a base or a derived type.
/// </para>
/// </remarks>
public sealed class UnknownType : IType
{
    public static readonly UnknownType Instance = new();
    public string DisplayName => "unknown";
    public TypeKind Kind => TypeKind.Unknown;

    private UnknownType() { }
}