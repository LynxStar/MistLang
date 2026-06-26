using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Metadata attached to <see cref="Types.UnionType"/> instances created by the Assembly Type Provider
/// to track whether the union originated from <c>Nullable&lt;T&gt;</c> or a reference type.
/// Used by the CSharp emitter to produce correct CLR-level signatures.
/// </summary>
public sealed class NullableProjectionInfo
{
    public NullableProjectionInfo(NullableOrigin origin, IType innerType)
    {
        Origin = origin;
        InnerType = innerType;
    }

    /// <summary>
    /// Whether this nullable projection originated from a <c>Nullable&lt;T&gt;</c> value type
    /// wrapper or from a reference type.
    /// </summary>
    public NullableOrigin Origin { get; }

    /// <summary>
    /// The inner (non-null) type before the <c>T | void</c> union was constructed.
    /// For <see cref="NullableOrigin.NullableValueType"/>, this is the <c>T</c> in <c>Nullable&lt;T&gt;</c>.
    /// For <see cref="NullableOrigin.ReferenceType"/>, this is the reference type itself.
    /// </summary>
    public IType InnerType { get; }
}