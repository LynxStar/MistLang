using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// The modifiers applied to a parameter, composed from three orthogonal primitives:
/// the <c>readonly</c> view restriction, the <c>ref</c> pass-by-reference primitive,
/// and the ownership tier.
/// </summary>
/// <remarks>
/// The primitives compose: <c>readonly ref</c> is simply <see cref="IsReadonly"/> +
/// <see cref="IsRef"/>, which is also the shape the "live" lending tier uses when the
/// argument is owned — the callee never needs to know it is participating in ownership.
/// The ownership tiers (<see cref="OwnershipKind.Borrowed"/>, <see cref="OwnershipKind.Frozen"/>,
/// <see cref="OwnershipKind.Owned"/>) add call-site guarantees beyond the primitives.
/// Validation of which combinations are legal, redundant, or invalid is performed by the
/// consuming builder/analyzer, not by this type.
/// </remarks>
/// <remarks>
/// <see cref="Direction"/> is NOT a fourth primitive and does not compose with the others: it is
/// what a CALL SITE must spell, where the rest describe the declaration. For a Mist parameter the
/// two agree and it is redundant. For a projected CLR one it is not derivable at all -- <c>[In] ref</c>
/// and a genuine <c>in</c> arrive as the same <see cref="IsRef"/> plus <see cref="IsReadonly"/> pair
/// and require opposite keywords -- which is the whole reason it is recorded rather than computed.
/// </remarks>
public readonly record struct ParameterModifier(
    bool IsReadonly,
    bool IsRef,
    OwnershipKind Ownership,
    ByRefDirection Direction = ByRefDirection.None);