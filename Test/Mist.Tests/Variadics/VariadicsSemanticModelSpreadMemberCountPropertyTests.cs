// Feature: variadics, task 10.8: property test for generated-member count over the CLASSIC
// (legacy Mist.Semantics) pipeline.
//
// Property 6: Generated-member count equals pack length.
// **Validates: Requirements 10.1, 10.6, 10.7, 10.8**
//
// For any member spread with stem S over a pack of length N, the declaration produces the
// aggregate member S of type (TArgs...) (the whole tuple) plus exactly N element-projection
// members S1..SN (1-based), where each projection Sk has the pack's element type at 0-based
// index k-1 and equals the k-th element of S, and both the aggregate and the projections take
// the spread's accessibility and member kind (field or property); an empty pack produces the
// empty aggregate and zero projections; and two spreads of the same pack in one type produce
// index-aligned projections at the same N.
//
// This is the CLASSIC-track counterpart of the ProgramModel-track property test
// (SpreadMemberCountPropertyTests, task 5.8). Where that test drives ProgramModelBuilder.Build
// and re-interns the aggregate tuple through the TypeInterner to read the projections off
// TupleType.PositionalFields, this one drives the legacy SemanticModelBuilder.Build -- the
// default, emitting pipeline -- and reads the recorded SemanticSpreadMember family (task 10.4)
// straight off the built type's SpreadMembers, then exercises the family's own projection rule:
//   * family.Stem / family.Kind (Field vs Property) / family.Pack record the declaration;
//   * family.AggregateMember is the real field/property symbol S, carrying the aggregate tuple
//     type (TArgs...), the declared member kind (SymbolKind.Field/Property), and the declared
//     accessibility (resolved from the declaration's modifiers in the model's accessibility
//     pass) -- so kind AND accessibility are asserted to be preserved (Requirement 10.1);
//   * family.MaterializeProjections(boundElementTypes) is the classic-model surface for the
//     count: given the pack's N bound element types it returns exactly N projections named
//     Stem1..StemN (1-based) with the element type at each index. This is the level at which
//     the count is validated in-process, because while the pack is symbolic the N named
//     projections are NOT materialized into member symbols (that is deferred to the type
//     checker's binding rule / backend monomorphization, tasks 12/19). Driving
//     MaterializeProjections with a generated bound Type[] of length N is the classic model's
//     documented projection path, not full-pipeline arity binding.
//
// It mirrors the construction/lookup pattern of the classic spread-member smoke tests
// (VariadicsSemanticModelSpreadMemberSmokeTests, task 10.4) and the classic property tests
// (VariadicsSemanticModelAritySlotCountPropertyTests task 10.6,
// VariadicsSemanticModelOneSugaredPackPropertyTests task 10.7), generalizing the smoke tests'
// fixed examples into a property over generated stem / kind / accessibility / pack length.
//
// Everything runs entirely IN-PROCESS via the shared VariadicsCompilerFixture.Parse helper plus
// SemanticModelBuilder.Build; nothing is emitted or run, so there are ZERO external process
// spawns. The bound element types passed to MaterializeProjections are constructed directly as
// distinct semantic types drawn from a small pool (Int32, String, Boolean, Double, Int64, Char)
// -- the classic parseable-primitive pool -- so per-index type correspondence is asserted by
// reference identity.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated single-spread scenario for the classic pipeline: a member spread declared with
/// the given <see cref="Stem"/>, member kind (<see cref="IsProperty"/>), and accessibility
/// (<see cref="AccessKeyword"/> / <see cref="ExpectedAccessibility"/>), together with a pack
/// length <see cref="PackLength"/> and the element-type pool indices (<see cref="ElementIndices"/>)
/// used to bind the pack to a concrete <c>Type[]</c> of that length when materializing the
/// projections.
/// </summary>
public sealed record SemanticSpreadMemberScenario(
    string Stem,
    bool IsProperty,
    string AccessKeyword,
    Accessibility ExpectedAccessibility,
    int PackLength,
    IReadOnlyList<int> ElementIndices)
{
    public override string ToString()
    {
        var kind = IsProperty ? "property" : "field";
        return $"{AccessKeyword} {kind} spread '{Stem}' over pack length {PackLength} "
            + $"[{string.Join(", ", ElementIndices)}]";
    }
}

/// <summary>
/// A generated correlated-spread scenario for the classic pipeline: two spreads
/// (<see cref="StemA"/>, <see cref="StemB"/>, guaranteed distinct and pairwise non-prefixing) of
/// the <b>same</b> pack in one type, together with the member kind, accessibility, and a pack
/// length / element indices for binding. Exercises Requirement 10.8 (two spreads of the same
/// pack correlate at the same N).
/// </summary>
public sealed record SemanticCorrelatedSpreadScenario(
    string StemA,
    string StemB,
    bool IsProperty,
    string AccessKeyword,
    Accessibility ExpectedAccessibility,
    int PackLength,
    IReadOnlyList<int> ElementIndices)
{
    public override string ToString()
    {
        var kind = IsProperty ? "property" : "field";
        return $"{AccessKeyword} {kind} spreads '{StemA}'/'{StemB}' over pack length {PackLength}";
    }
}

/// <summary>
/// Property test for <b>Property 6 (Generated-member count equals pack length)</b> over the
/// classic (legacy <c>Mist.Semantics</c>) pipeline. Builds the semantic model in-process via
/// <see cref="SemanticModelBuilder"/> to read the recorded <see cref="SemanticSpreadMember"/>
/// family (stem, member kind, accessibility, aggregate tuple, and -- for correlated spreads --
/// the shared pack), then drives the family's <see cref="SemanticSpreadMember.MaterializeProjections"/>
/// with a generated bound <c>Type[]</c> of length <c>N</c> to assert exactly <c>N</c> element
/// projections named <c>Stem1 .. StemN</c> with the element type at each index (<c>N = 0</c> yields
/// zero; <c>N = 1</c> yields <c>Stem1</c>). Zero external process spawns.
/// <b>Validates: Requirements 10.1, 10.6, 10.7, 10.8.</b>
/// </summary>
public sealed class VariadicsSemanticModelSpreadMemberCountPropertyTests
{
    private const int PoolSize = 6;
    private const int MaxPackLength = 6;
    private const string HolderTypeName = "Holder";
    private const string SpreadStemCollisionId = "MIST_VAR_0007";
    private const string EmptySpreadStemId = "MIST_VAR_0008";

    // Stems for the single-spread scenarios. Any one is safe as the sole member: its stem
    // namespace reserves Stem and Stem{i}, and there is no other member to collide with.
    private static readonly string[] SingleStems =
        ["Item", "Slot", "Value", "Elem", "Cell", "Node"];

    // Stems for correlated-spread scenarios: pairwise non-prefixing, so any two distinct choices
    // never trip the stem-collision diagnostic MIST_VAR_0007.
    private static readonly string[] CorrelatedStems =
        ["Key", "Value", "Alpha", "Beta", "Gamma", "Left", "Right", "Head"];

    // Accessibility keyword paired with the classic Accessibility the model must record for it.
    private static readonly (string Keyword, Accessibility Access)[] AccessChoices =
    [
        ("public", Accessibility.Public),
        ("private", Accessibility.Private),
        ("internal", Accessibility.Internal),
        ("protected", Accessibility.Protected),
    ];

    // ─── FsCheck Arbitrary registrations ─────────────────────────────────────

    /// <summary>Generator for single-spread scenarios (stem, kind, accessibility, N, elements).</summary>
    public static Arbitrary<SemanticSpreadMemberScenario> SpreadScenarios()
    {
        var genElementIndex = Gen.Choose(0, PoolSize - 1);

        var gen =
            from stem in Gen.Elements(SingleStems)
            from isProperty in Gen.Elements(true, false)
            from access in Gen.Elements(AccessChoices)
            from n in Gen.Choose(0, MaxPackLength)
            from indices in Gen.ArrayOf(genElementIndex, n)
            select new SemanticSpreadMemberScenario(stem, isProperty, access.Keyword, access.Access, n, indices);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generator for correlated-spread scenarios. Picks two distinct indices into
    /// <see cref="CorrelatedStems"/> (via a non-zero offset modulo the pool size) so the two
    /// stems are always distinct and, because the pool is pairwise non-prefixing, never collide.
    /// </summary>
    public static Arbitrary<SemanticCorrelatedSpreadScenario> CorrelatedScenarios()
    {
        var genElementIndex = Gen.Choose(0, PoolSize - 1);

        var gen =
            from indexA in Gen.Choose(0, CorrelatedStems.Length - 1)
            from offset in Gen.Choose(1, CorrelatedStems.Length - 1)
            from isProperty in Gen.Elements(true, false)
            from access in Gen.Elements(AccessChoices)
            from n in Gen.Choose(0, MaxPackLength)
            from indices in Gen.ArrayOf(genElementIndex, n)
            let indexB = (indexA + offset) % CorrelatedStems.Length
            select new SemanticCorrelatedSpreadScenario(
                CorrelatedStems[indexA],
                CorrelatedStems[indexB],
                isProperty,
                access.Keyword,
                access.Access,
                n,
                indices);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: for a member spread with stem S over a pack of length N, the classic model
    // records S/kind/accessibility, the aggregate is (TArgs...), and materializing the family
    // with a bound Type[] of length N yields exactly N element projections Stem1..StemN whose
    // types are the bound elements in order (N=0 -> zero, R10.6; N=1 -> one, R10.7).
    //
    // **Validates: Requirements 10.1, 10.6, 10.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelSpreadMemberCountPropertyTests) }, MaxTest = 100)]
    public bool GeneratedMemberCount_EqualsPackLength_OverLegacyModel(SemanticSpreadMemberScenario scenario)
    {
        var (model, diagnostics) = BuildModel(BuildSingleSource(scenario));

        // A well-formed lone spread must not trip either spread-family diagnostic.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadStemCollisionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == EmptySpreadStemId);

        var holder = FindType<ClassType>(model, HolderTypeName);
        var pack = Assert.IsType<TypeParameterType>(holder.TypeParameters[0]);
        Assert.True(pack.IsPack);

        // ── Requirement 10.1: the family records the stem, the member kind, and the pack. ──
        var family = Assert.Single(holder.SpreadMembers);
        Assert.Equal(scenario.Stem, family.Stem);
        Assert.Equal(
            scenario.IsProperty ? SpreadMemberKind.Property : SpreadMemberKind.Field,
            family.Kind);
        Assert.Same(pack, family.Pack);

        // ── The aggregate is S : (TArgs...): the tuple over the symbolic pack. ──
        var symbolicElement = Assert.Single(family.AggregateType.ElementTypes);
        Assert.Same(pack, symbolicElement);

        // ── The aggregate member S is surfaced with the aggregate tuple type, the declared
        //    member kind (field vs property), and the declared accessibility (Requirement 10.1). ──
        var aggregate = family.AggregateMember;
        Assert.Equal(scenario.Stem, aggregate.Name);
        Assert.Equal(
            scenario.IsProperty ? SymbolKind.Property : SymbolKind.Field,
            aggregate.Kind);
        Assert.Equal(scenario.ExpectedAccessibility, aggregate.Accessibility);
        Assert.Same(family.AggregateType, aggregate.DeclaredType);

        // ── Materialize the projections for a bound pack of length N and assert exactly N,
        //    named Stem1..StemN (1-based), with the bound element type at each index.
        //    Empty pack -> zero (R10.6); length-1 -> the single Stem1 (R10.7). ──
        var elements = BuildElements(scenario.ElementIndices);
        var projections = family.MaterializeProjections(elements);

        Assert.Equal(scenario.PackLength, projections.Count);
        for(var k = 1; k <= scenario.PackLength; k++)
        {
            // Projection Sk is named Stem + 1-based index and carries the pack element type at
            // 0-based index k-1 (asserted by reference identity against the bound input).
            Assert.Equal($"{scenario.Stem}{k}", projections[k - 1].Name);
            Assert.Equal(family.ProjectionName(k), projections[k - 1].Name);
            Assert.Same(elements[k - 1], projections[k - 1].Type);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6 (correlation): two spreads of the SAME pack produce two families over one
    // shared pack, so materializing both with the same bound Type[] of length N yields
    // index-aligned projections A1..AN / B1..BN at the same N.
    //
    // **Validates: Requirement 10.8**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelSpreadMemberCountPropertyTests) }, MaxTest = 100)]
    public bool CorrelatedSpreads_ShareOnePackAndAlignAtN_OverLegacyModel(SemanticCorrelatedSpreadScenario scenario)
    {
        var (model, diagnostics) = BuildModel(BuildCorrelatedSource(scenario));

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadStemCollisionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == EmptySpreadStemId);

        var holder = FindType<ClassType>(model, HolderTypeName);
        var pack = Assert.IsType<TypeParameterType>(holder.TypeParameters[0]);

        // Requirement 10.8: two spreads -> two families, both over the SAME pack instance.
        Assert.Equal(2, holder.SpreadMembers.Count);
        var famA = holder.SpreadMembers.Single(s => s.Stem == scenario.StemA);
        var famB = holder.SpreadMembers.Single(s => s.Stem == scenario.StemB);
        Assert.Same(pack, famA.Pack);
        Assert.Same(pack, famB.Pack);
        Assert.NotEqual(scenario.StemA, scenario.StemB);

        // Because they share the pack, binding the same Type[] of length N yields index-aligned
        // projections: A_1..A_N and B_1..B_N over the same element type at each index.
        var elements = BuildElements(scenario.ElementIndices);
        var aProjections = famA.MaterializeProjections(elements);
        var bProjections = famB.MaterializeProjections(elements);

        Assert.Equal(scenario.PackLength, aProjections.Count);
        Assert.Equal(aProjections.Count, bProjections.Count);
        for(var k = 1; k <= scenario.PackLength; k++)
        {
            Assert.Equal($"{scenario.StemA}{k}", aProjections[k - 1].Name);
            Assert.Equal($"{scenario.StemB}{k}", bProjections[k - 1].Name);
            Assert.Same(elements[k - 1], aProjections[k - 1].Type);
            Assert.Same(elements[k - 1], bProjections[k - 1].Type);
        }

        return true;
    }

    // ─── Source construction ────────────────────────────────────────────────

    private static string BuildSingleSource(SemanticSpreadMemberScenario scenario)
    {
        var member = MemberLine(scenario.IsProperty, scenario.AccessKeyword, scenario.Stem);
        return $"public class {HolderTypeName}<params TArgs>\n{{\n{member}\n}}";
    }

    private static string BuildCorrelatedSource(SemanticCorrelatedSpreadScenario scenario)
    {
        var memberA = MemberLine(scenario.IsProperty, scenario.AccessKeyword, scenario.StemA);
        var memberB = MemberLine(scenario.IsProperty, scenario.AccessKeyword, scenario.StemB);
        return $"public class {HolderTypeName}<params TArgs>\n{{\n{memberA}\n{memberB}\n}}";
    }

    private static string MemberLine(bool isProperty, string accessKeyword, string stem)
    {
        return isProperty
            ? $"    {accessKeyword} TArgs... {stem} {{ get; }}"
            : $"    {accessKeyword} TArgs... {stem};";
    }

    // ─── Element-type pool for the bound Type[] passed to MaterializeProjections ──────────────

    /// <summary>
    /// Builds the bound element types for a pack of the generated length by indexing into the
    /// pool. Distinct pool instances let the per-index type correspondence be asserted by
    /// reference identity (<see cref="SemanticSpreadMember.MaterializeProjections"/> echoes the
    /// supplied element types into the projections).
    /// </summary>
    private static IType[] BuildElements(IReadOnlyList<int> indices)
    {
        var pool = BuildElementPool();
        var result = new IType[indices.Count];

        for(var i = 0; i < indices.Count; i++)
        {
            result[i] = pool[indices[i]];
        }

        return result;
    }

    private static ClassType[] BuildElementPool()
    {
        return
        [
            ElementType("Int32"),
            ElementType("String"),
            ElementType("Boolean"),
            ElementType("Double"),
            ElementType("Int64"),
            ElementType("Char"),
        ];
    }

    /// <summary>Builds a simple, distinct element type for the bound pack.</summary>
    private static ClassType ElementType(string name)
    {
        return new ClassType(name, new Scope(null), "Standard");
    }

    // ─── In-process legacy semantic-model construction and lookup (zero process spawns) ───────

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely in-process
    /// (no stdlib, no analyzers, no emit, zero process spawns), returning the model together with
    /// every construction-stage diagnostic. Spread-member families and the aggregate member's
    /// accessibility are decided during model construction, so inspecting the recorded families
    /// and construction diagnostics is sufficient.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Generated source is expected to parse without lex/parse errors: {source}");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        Assert.NotNull(model);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Finds the single source type named <paramref name="name"/> whose declared type is a
    /// <typeparamref name="T"/> (for example <see cref="ClassType"/>).
    /// </summary>
    private static T FindType<T>(ISemanticModel model, string name)
        where T : class, IType
    {
        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == name && s.DeclaredType is T);
        Assert.NotNull(symbol);
        return (T)symbol!.DeclaredType!;
    }
}