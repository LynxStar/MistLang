// Feature: unknown-type (Phase B, Task 9.3 + 9.4)
// **Validates: Requirements 4.1, 4.2, 4.4, 4.5**
//
// Property-based and unit tests for the UnknownType top-type assignability rule
// added to TypeCheckerAnalyzer.IsAssignable. The rule has four parts:
//
//   Req 4.1 — every non-`void` source type is assignable to UnknownType.
//   Req 4.2 — `void` is NOT assignable to UnknownType.
//   Req 4.4 — `unknown` is NOT assignable to a non-`unknown` target.
//   Req 4.5 — `unknown` → `unknown` is accepted.
//
// These tests exercise IsAssignable directly (no compilation, no process
// spawning), as permitted by the InternalsVisibleTo directive on Mist.csproj.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for assignability into <see cref="UnknownType"/>.
///
/// <para>
/// <strong>Property 2 (unknown-type feature):</strong> every non-<c>void</c>
/// type is assignable to <c>UnknownType</c>; <c>void</c> is not.
/// <strong>Validates: Requirements 4.1, 4.2</strong>
/// </para>
///
/// <para>
/// The test runs entirely in-process against <c>IsAssignable</c>: it
/// constructs a small pool of <see cref="IType"/> values (primitives via
/// <see cref="LegacyPrimitiveTypes"/>, a user-defined class, a user-defined
/// struct, an array type, and a constructed generic) and asserts the top-type
/// rule for every pool element.
/// </para>
/// </summary>
public class UnknownAssignabilityPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Type pool
    //
    // Each generator below returns one candidate IType. The pool covers the
    // type shapes the spec calls out (primitives, class, struct, array,
    // constructed generic) plus VoidType as a dedicated tag so the property
    // test can distinguish the Req 4.2 rejection case.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Classification of a generated candidate so the property test knows
    /// whether to expect assignability into <c>unknown</c> (the non-<c>void</c>
    /// case, Req 4.1) or non-assignability (the <c>void</c> case, Req 4.2).
    /// </summary>
#pragma warning disable CA1034 // Nested type is a test fixture DTO.
    public enum VoidFlag
    {
        NonVoid,
        Void,
    }

    /// <summary>
    /// A candidate <see cref="IType"/> paired with its void-ness.
    /// </summary>
    public sealed record TypeCandidate(IType Type, VoidFlag Flag)
    {
        public override string ToString()
        {
            return $"{Type.DisplayName} ({Flag})";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Shared generic class definition used to construct a generic type in the
    /// pool. Built lazily so FsCheck's shrinker and sample collection share a
    /// single instance (reference identity is what ConstructedType compares).
    /// </summary>
    private static readonly ClassType SampleListDefinition = BuildSampleListDefinition();

    private static ClassType BuildSampleListDefinition()
    {
        var scope = new Scope(parent: null);
        var list = new ClassType("List", scope, containingNamespace: "TestPool")
        {
            TypeParameters = new[] { new TypeParameterType("T", 0) },
        };
        return list;
    }

    /// <summary>
    /// User-defined class / struct singletons; reused across generated
    /// samples so assignability identity checks behave consistently.
    /// </summary>
    private static readonly ClassType SampleClass =
        new("SampleClass", new Scope(parent: null), containingNamespace: "TestPool");

    private static readonly StructType SampleStruct =
        new("SampleStruct", new Scope(parent: null), containingNamespace: "TestPool");

    /// <summary>Returns the full candidate pool the PBT samples over.</summary>
    private static TypeCandidate[] BuildCandidatePool()
    {
        return new[]
        {
            // Primitives via the legacy primitive singletons (these exist
            // even without the stdlib loaded and are the simplest IType
            // values available to a unit test).
            new TypeCandidate(LegacyPrimitiveTypes.Int, VoidFlag.NonVoid),
            new TypeCandidate(LegacyPrimitiveTypes.Bool, VoidFlag.NonVoid),
            new TypeCandidate(LegacyPrimitiveTypes.String, VoidFlag.NonVoid),
            new TypeCandidate(LegacyPrimitiveTypes.Float, VoidFlag.NonVoid),
            new TypeCandidate(LegacyPrimitiveTypes.Char, VoidFlag.NonVoid),

            // User-defined class and struct.
            new TypeCandidate(SampleClass, VoidFlag.NonVoid),
            new TypeCandidate(SampleStruct, VoidFlag.NonVoid),

            // Array of primitive and array of user-defined class.
            new TypeCandidate(new ArrayType(LegacyPrimitiveTypes.Int), VoidFlag.NonVoid),
            new TypeCandidate(new ArrayType(SampleClass), VoidFlag.NonVoid),

            // Constructed generic: List<int> and List<SampleClass>.
            new TypeCandidate(
                new ConstructedType(SampleListDefinition, new IType[] { LegacyPrimitiveTypes.Int }),
                VoidFlag.NonVoid),
            new TypeCandidate(
                new ConstructedType(SampleListDefinition, new IType[] { SampleClass }),
                VoidFlag.NonVoid),

            // Void — the dedicated rejection case (Req 4.2).
            new TypeCandidate(LegacyPrimitiveTypes.Void, VoidFlag.Void),
        };
    }

    /// <summary>
    /// FsCheck <see cref="Arbitrary{T}"/> that draws from
    /// <see cref="BuildCandidatePool"/>. A small, fixed pool is sufficient
    /// for this property: the universe of interest is the distinct IType
    /// <em>shapes</em> (primitive, class, struct, array, constructed), not
    /// arbitrary type parameters.
    /// </summary>
    public static Arbitrary<TypeCandidate> ArbitraryTypeCandidate()
    {
        return Gen.Elements(BuildCandidatePool()).ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2 — Every non-void type is assignable to UnknownType;
    //              void is not.
    // **Validates: Requirements 4.1, 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(UnknownAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool EveryNonVoidType_IsAssignableToUnknown_AndVoidIsNot(TypeCandidate candidate)
    {
        var result = TypeCheckerAnalyzer.IsAssignable(candidate.Type, UnknownType.Instance);
        return candidate.Flag switch
        {
            VoidFlag.NonVoid => result,   // Req 4.1: every non-void type must be accepted.
            VoidFlag.Void => !result,     // Req 4.2: void must be rejected.
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 9.4 — Unit tests for the reverse direction and identity.
    //
    // The property test above exercises Req 4.1 and 4.2 at scale. The unit
    // tests below pin down the discrete cases required by Req 4.4 (unknown
    // is not assignable to a non-unknown target) and Req 4.5 (unknown →
    // unknown is accepted).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Req 4.4: an <c>unknown</c>-typed value is not assignable to a
    /// primitive-typed target without first narrowing via pattern matching.
    /// </summary>
    [Fact]
    public void Reverse_Unknown_To_Int_IsNotAssignable()
    {
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(UnknownType.Instance, LegacyPrimitiveTypes.Int),
            "Unknown must not be assignable to int — Req 4.4 requires 'is' pattern narrowing.");
    }

    /// <summary>
    /// Req 4.4: an <c>unknown</c>-typed value is not assignable to a
    /// user-defined class-typed target either.
    /// </summary>
    [Fact]
    public void Reverse_Unknown_To_Class_IsNotAssignable()
    {
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(UnknownType.Instance, SampleClass),
            "Unknown must not be assignable to a user-defined class — Req 4.4 requires 'is' pattern narrowing.");
    }

    /// <summary>
    /// Req 4.5: <c>unknown</c> → <c>unknown</c> is identity-accepted.
    /// </summary>
    [Fact]
    public void Unknown_To_Unknown_IsAssignable()
    {
        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(UnknownType.Instance, UnknownType.Instance),
            "Unknown → unknown must be accepted (Req 4.5: identity).");
    }

    /// <summary>
    /// Req 4.2 pinned down as a deterministic unit test in addition to the
    /// PBT coverage above — assigning <c>void</c> to <c>unknown</c> is rejected.
    /// </summary>
    [Fact]
    public void Void_To_Unknown_IsNotAssignable()
    {
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(LegacyPrimitiveTypes.Void, UnknownType.Instance),
            "Void must not be assignable to unknown (Req 4.2: void carries no value).");
    }

    /// <summary>
    /// Req 4.1 pinned down as a deterministic unit test alongside the PBT
    /// coverage above — assigning a primitive to <c>unknown</c> is accepted.
    /// </summary>
    [Fact]
    public void Int_To_Unknown_IsAssignable()
    {
        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(LegacyPrimitiveTypes.Int, UnknownType.Instance),
            "Every non-void type, including int, must be assignable to unknown (Req 4.1).");
    }
}