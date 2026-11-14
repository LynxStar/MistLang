using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// Three orthogonal primitives: the <c>readonly</c> view restriction, <c>ref</c>, and the ownership
/// tier. <c>readonly ref</c> is simply both flags, which is also the shape the live lending tier uses
/// for an owned argument, so the callee never knows it takes part in ownership. Which combinations are
/// legal is the consuming builder's or analyzer's to judge.
/// </summary>
/// <remarks>
/// <see cref="Direction"/> is not a fourth primitive: it is what a CALL SITE must spell, where the rest
/// describe the declaration. For a Mist parameter the two agree; for a projected CLR one it is not
/// derivable, since <c>[In] ref</c> and a genuine <c>in</c> arrive as the same flag pair and require
/// opposite keywords.
/// </remarks>
public readonly record struct ParameterModifier(
    bool IsReadonly,
    bool IsRef,
    OwnershipKind Ownership,
    ByRefDirection Direction = ByRefDirection.None);