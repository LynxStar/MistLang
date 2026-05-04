// Feature: unknown-type (Phase B, Task 5.2)
// Validates Requirements 3.2 and 3.4 — the user-facing UnknownType is a singleton
// with the expected display name and kind, and it has no declared members.

using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the <see cref="UnknownType"/> singleton introduced in Phase B of
/// the unknown-type feature. These tests pin down the singleton identity, the
/// public surface (display name, kind), and the "no declared members" invariant
/// required by Requirement 3.4.
/// </summary>
public class UnknownTypeTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.2 — UnknownType is a singleton accessible via Instance
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Instance_Equals_Itself()
    {
        // The singleton must be reference-equal to itself across accesses:
        // every call site that reads UnknownType.Instance sees the same object.
        Assert.Same(UnknownType.Instance, UnknownType.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.3 — UnknownType reports "unknown" for pretty-printing
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DisplayName_Is_unknown()
    {
        Assert.Equal("unknown", UnknownType.Instance.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1 — UnknownType's kind is TypeKind.Unknown
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Kind_Is_TypeKind_Unknown()
    {
        Assert.Equal(TypeKind.Unknown, UnknownType.Instance.Kind);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.4 — UnknownType has no declared members
    //
    // UnknownType is intentionally a bare IType: it exposes only DisplayName
    // and Kind (from the IType contract). In particular, it does NOT carry a
    // MemberScope property (the types that do — ClassType, StructType,
    // InterfaceType, TraitType — declare MemberScope on themselves, not on
    // IType). Verifying the absence of a MemberScope property on the concrete
    // UnknownType class is an idiomatic structural check of "no declared
    // members."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Has_No_Member_Scope()
    {
        // UnknownType must not define a MemberScope property. If a future change
        // introduces one, this test forces a conversation about whether the
        // "no declared members" invariant from Req 3.4 still holds.
        //
        // Note: System.Reflection.PropertyInfo is fully qualified to avoid a
        // name collision with Mist.Semantics.Contracts.PropertyInfo, which is
        // the compiler's semantic model record for a Mist property.
        System.Reflection.PropertyInfo? memberScope = typeof(UnknownType).GetProperty(
            "MemberScope",
            System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);

        Assert.Null(memberScope);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Small invariant tests — lock in the singleton encoding so that the
    // "Instance is the only way to get one" contract cannot be silently broken.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnknownType_Is_Sealed()
    {
        Assert.True(
            typeof(UnknownType).IsSealed,
            "UnknownType must be sealed: the singleton encoding depends on no subclass ever being declared.");
    }

    [Fact]
    public void UnknownType_Has_No_Public_Constructor()
    {
        // A public constructor would defeat the singleton pattern — callers could
        // construct an independent instance that is !ReferenceEquals to
        // UnknownType.Instance, breaking every identity check elsewhere in the
        // compiler.
        System.Reflection.ConstructorInfo[] publicCtors = typeof(UnknownType).GetConstructors(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.Empty(publicCtors);
    }
}
