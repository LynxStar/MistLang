// Feature: assembly-type-provider, Property 7: Nullable and Reference Type Projection
// **Validates: Requirements 14.1, 14.2, 14.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Describes a CLR type scenario for nullable projection testing.
/// Each scenario carries the CLR type and the expected projection category.
/// </summary>
public sealed class NullableProjectionScenario
{
    public Type ClrType { get; }
    public NullableCategory ExpectedCategory { get; }
    public string Description { get; }

    public NullableProjectionScenario(Type clrType, NullableCategory expectedCategory, string description)
    {
        ClrType = clrType;
        ExpectedCategory = expectedCategory;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// The expected projection category for a CLR type in a member signature.
/// </summary>
public enum NullableCategory
{
    /// <summary>Nullable&lt;T&gt; → UnionType with NullableValueType origin.</summary>
    NullableValueType,

    /// <summary>Reference type → UnionType with ReferenceType origin.</summary>
    ReferenceType,

    /// <summary>Non-nullable value type → plain type (no union).</summary>
    PlainValueType,
}

/// <summary>
/// Property-based tests for nullable and reference type projection.
///
/// Property 7: For any CLR type appearing in a member signature:
/// - If it is Nullable&lt;T&gt;, the projected type SHALL be T | void with NullableValueType origin
/// - If it is a reference type (class, interface, string, array, delegate), the projected type SHALL be T | void with ReferenceType origin
/// - If it is a non-nullable value type, the projected type SHALL be the plain Mist type without a union
///
/// **Validates: Requirements 14.1, 14.2, 14.5**
/// </summary>
public sealed class NullableProjectionPropertyTests
{
    private static readonly TypeProjector Projector =
        new(new IntrinsicTypeMap(), new VisibilityFilter());

    private static readonly FallbackStdlibTypeProvider Stdlib = FallbackStdlibTypeProvider.Instance;

    // ─── CLR type pools for generation ──────────────────────────────────

    /// <summary>
    /// Nullable value types: Nullable&lt;T&gt; wrappers around various value types.
    /// These should all project as UnionType with NullableValueType origin.
    /// </summary>
    private static readonly Type[] NullableValueTypes =
    [
        typeof(int?),
        typeof(bool?),
        typeof(byte?),
        typeof(short?),
        typeof(long?),
        typeof(float?),
        typeof(double?),
        typeof(decimal?),
        typeof(char?),
        typeof(uint?),
        typeof(ulong?),
        typeof(ushort?),
        typeof(sbyte?),
        typeof(Mist.Tests.InteropFixtures.SimpleEnum?),
        typeof(Mist.Tests.InteropFixtures.SimpleStruct?),
    ];

    /// <summary>
    /// Reference types: classes, interfaces, strings, arrays, delegates.
    /// These should all project as UnionType with ReferenceType origin.
    /// </summary>
    private static readonly Type[] ReferenceTypes =
    [
        typeof(string),
        typeof(object),
        typeof(int[]),
        typeof(string[]),
        typeof(Mist.Tests.InteropFixtures.SimpleClass),
        typeof(Mist.Tests.InteropFixtures.ISimpleInterface),
        typeof(System.Collections.Generic.List<int>),
        typeof(System.Collections.Generic.Dictionary<string, int>),
        typeof(System.IComparable),
        typeof(System.Action),
        typeof(System.Func<int>),
        typeof(System.EventHandler),
        typeof(Exception),
        typeof(System.IO.Stream),
    ];

    /// <summary>
    /// Non-nullable value types: plain structs and enums.
    /// These should project as plain Mist types without a union.
    /// </summary>
    private static readonly Type[] PlainValueTypes =
    [
        typeof(int),
        typeof(bool),
        typeof(byte),
        typeof(short),
        typeof(long),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(char),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
        typeof(sbyte),
        typeof(Mist.Tests.InteropFixtures.SimpleStruct),
        typeof(Mist.Tests.InteropFixtures.SimpleEnum),
        typeof(System.DateTime),
        typeof(System.Guid),
        typeof(System.TimeSpan),
    ];

    // ─── FsCheck generator ──────────────────────────────────────────────

    public static Arbitrary<NullableProjectionScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<NullableProjectionScenario> GenScenario()
    {
        var genNullable = Gen.Elements(NullableValueTypes)
            .Select(t => new NullableProjectionScenario(
                t, NullableCategory.NullableValueType,
                $"Nullable<{Nullable.GetUnderlyingType(t)!.Name}>"));

        var genReference = Gen.Elements(ReferenceTypes)
            .Select(t => new NullableProjectionScenario(
                t, NullableCategory.ReferenceType,
                $"Reference({t.Name})"));

        var genPlainValue = Gen.Elements(PlainValueTypes)
            .Select(t => new NullableProjectionScenario(
                t, NullableCategory.PlainValueType,
                $"PlainValue({t.Name})"));

        // Combine all three categories with equal probability
        return Gen.OneOf(genNullable, genReference, genPlainValue);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Nullable and Reference Type Projection
    //
    // **Validates: Requirements 14.1, 14.2, 14.5**
    //
    // For any CLR type appearing in a member signature:
    // - Nullable<T> → UnionType with NullableValueType origin
    // - Reference type → UnionType with ReferenceType origin
    // - Non-nullable value type → plain type (no union)
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(NullableProjectionPropertyTests) })]
    public Property NullableProjection_CorrectCategoryBasedOnTypeKind(NullableProjectionScenario scenario)
    {
        var result = Projector.ProjectNullableType(scenario.ClrType, Stdlib);

        return scenario.ExpectedCategory switch
        {
            NullableCategory.NullableValueType => VerifyNullableValueTypeProjection(result, scenario),
            NullableCategory.ReferenceType => VerifyReferenceTypeProjection(result, scenario),
            NullableCategory.PlainValueType => VerifyPlainValueTypeProjection(result, scenario),
            _ => false.ToProperty().Label($"Unknown category: {scenario.ExpectedCategory}"),
        };
    }

    // ─── Verification helpers ───────────────────────────────────────────

    private static Property VerifyNullableValueTypeProjection(IType result, NullableProjectionScenario scenario)
    {
        if(result is not UnionType union)
        {
            return false.ToProperty()
                .Label($"Expected UnionType for Nullable<T> but got {result.GetType().Name}: {scenario}");
        }

        if(union.Constituents.Count != 2)
        {
            return false.ToProperty()
                .Label($"Expected 2 constituents (T | void) but got {union.Constituents.Count}: {scenario}");
        }

        if(union.NullableProjection is null)
        {
            return false.ToProperty()
                .Label($"Expected NullableProjection metadata but was null: {scenario}");
        }

        if(union.NullableProjection.Origin != NullableOrigin.NullableValueType)
        {
            return false.ToProperty()
                .Label($"Expected NullableValueType origin but got {union.NullableProjection.Origin}: {scenario}");
        }

        // Verify one constituent is void
        var hasVoid = union.Constituents.Any(c => c.DisplayName == "void");
        if(!hasVoid)
        {
            return false.ToProperty()
                .Label($"Expected void constituent in union but not found: {scenario}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }

    private static Property VerifyReferenceTypeProjection(IType result, NullableProjectionScenario scenario)
    {
        if(result is not UnionType union)
        {
            return false.ToProperty()
                .Label($"Expected UnionType for reference type but got {result.GetType().Name}: {scenario}");
        }

        if(union.Constituents.Count != 2)
        {
            return false.ToProperty()
                .Label($"Expected 2 constituents (T | void) but got {union.Constituents.Count}: {scenario}");
        }

        if(union.NullableProjection is null)
        {
            return false.ToProperty()
                .Label($"Expected NullableProjection metadata but was null: {scenario}");
        }

        if(union.NullableProjection.Origin != NullableOrigin.ReferenceType)
        {
            return false.ToProperty()
                .Label($"Expected ReferenceType origin but got {union.NullableProjection.Origin}: {scenario}");
        }

        // Verify one constituent is void
        var hasVoid = union.Constituents.Any(c => c.DisplayName == "void");
        if(!hasVoid)
        {
            return false.ToProperty()
                .Label($"Expected void constituent in union but not found: {scenario}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }

    private static Property VerifyPlainValueTypeProjection(IType result, NullableProjectionScenario scenario)
    {
        if(result is UnionType union)
        {
            return false.ToProperty()
                .Label($"Expected plain type (no union) for non-nullable value type but got UnionType " +
                       $"with {union.Constituents.Count} constituents: {scenario}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }
}