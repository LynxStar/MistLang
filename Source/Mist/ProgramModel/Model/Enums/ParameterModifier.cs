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
public readonly record struct ParameterModifier(
    bool IsReadonly,
    bool IsRef,
    OwnershipKind Ownership);