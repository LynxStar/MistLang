// Feature: unknown-type
// Validates Requirement 3.1 — the semantic model exposes a distinct TypeKind
// value for the user-facing unknown type and, per the TypeKind-audit decision
// recorded on ConstructedType.MemberScope, the existing throw branch remains
// correct when the generic definition is not a ClassType/StructType/AbstractType
// (the unknown top type is never a generic definition in practice).

using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the <see cref="TypeKind"/> enum and the cross-cutting invariants
/// documented in the unknown-type design (Phase B, Req 3.1 and the audit of
/// <see cref="ConstructedType.MemberScope"/>).
/// </summary>
public class TypeKindTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1 — TypeKind.Unknown is a defined variant placed after Enum
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeKind_Unknown_Variant_Exists()
    {
        Assert.True(
            Enum.IsDefined(typeof(TypeKind), TypeKind.Unknown),
            "TypeKind.Unknown must be a defined enum value.");

        // The design (Phase B, "New TypeKind value") specifies the variant is
        // placed after Enum. Lock that ordering in so a reorder is caught.
        Assert.True(
            (int)TypeKind.Unknown > (int)TypeKind.Enum,
            $"TypeKind.Unknown ({(int)TypeKind.Unknown}) must appear after TypeKind.Enum ({(int)TypeKind.Enum}) in the enum.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1 — ConstructedType.MemberScope throw branch (audit note)
    //
    // The design records that the existing throw in ConstructedType.MemberScope
    // remains correct after TypeKind.Unknown is added, because UnknownType is
    // never the generic definition of a ConstructedType. This test exercises
    // the throw branch with an IType whose Kind is TypeKind.Unknown to prove
    // the switch default still fires and the invariant is enforced at runtime.
    //
    // Note: the user-facing UnknownType singleton class does not exist yet
    // (Task 5.1 creates it). We use a minimal stand-in IType with
    // Kind == TypeKind.Unknown so this test does not depend on Task 5.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructedType_MemberScope_Throws_When_GenericDefinition_Is_Not_ClassStructAbstract()
    {
        IType standInDefinition = new StandInUnknownType();
        var constructed = new ConstructedType(standInDefinition, Array.Empty<IType>());

        var ex = Assert.Throws<InvalidOperationException>(() => _ = constructed.MemberScope);
        Assert.Contains("ClassType", ex.Message, StringComparison.Ordinal);
        Assert.Contains("StructType", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AbstractType", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Minimal IType stand-in used only to drive the throw branch of
    /// <see cref="ConstructedType.MemberScope"/>. Not representative of the
    /// real user-facing UnknownType singleton (Task 5.1).
    /// </summary>
    private sealed class StandInUnknownType : IType
    {
        public string DisplayName => "unknown";
        public TypeKind Kind => TypeKind.Unknown;
    }
}
