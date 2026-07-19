// Feature: variadics, task 5.8 - property test for generated-member count.
//
// Property 6: Generated-member count equals pack length.
// **Validates: Requirements 10.1, 10.6, 10.7, 10.8**
//
// For any member spread with stem S over a pack of length N, the declaration produces the
// aggregate member S of type (TArgs...) (the whole tuple) plus exactly N element-projection
// members S1..SN (1-based), where each projection Sk has the pack's element type at 0-based
// index k-1 and equals the k-th element of S, and both the aggregate and the projections take
// the spread's accessibility and member kind; an empty pack produces the empty aggregate and
// zero projections; and two spreads of the same pack in one type produce index-aligned
// projections at the same N.
//
// ── Materialization level (read the code first, then chose the available level) ──
// Task 5.4 recorded the spread-generated member FAMILY in the program model: a SpreadMember
// captures the stem, the (symbolic) pack, the aggregate tuple type (TArgs...), the member kind
// (field/property), and the accessibility. Crucially, while the pack is SYMBOLIC the N element
// projections are NOT enumerated -- per SpreadMember's own contract, the aggregate (TArgs...)
// carries the pack as its single symbolic element and re-interns into an N-element tuple only
// when the pack binds a concrete Type[], so "the projections follow the aggregate tuple's
// positional fields once bound." Per-arity NAMED projection materialization (surfacing the
// Stem1..StemN members) is backend monomorphization (task 14.1), which is not implemented yet;
// neither is the type-checker binding rule (task 7.1). So the N-named-projection members cannot
// be read end-to-end in-process at this stage.
//
// This test therefore validates Property 6 at the level that IS available in-process, driving
// the two mechanisms that exist:
//   (1) the real family metadata from ProgramModelBuilder.Build -- stem, member kind,
//       accessibility, the aggregate member, and (for correlated spreads) the shared pack; and
//   (2) the documented projection-materialization path: binding the aggregate to a concrete
//       Type[] of length N re-interns it into the N-element tuple whose positional fields
//       Item1..ItemN back the element projections Stem1..StemN. The count and per-index element
//       types are read off TupleType.PositionalFields -- exactly the fields the design says the
//       projections follow.
//
// Everything runs entirely in-process: ProgramModelBuilder.Build and the TypeInterner are
// driven directly, with ZERO external process spawns (matching the task 4.4 identity property
// test and the task 5.4 spread-member smoke test, which drive the program model in-process).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated single-spread scenario: a member spread declared with the given
/// <see cref="Stem"/>, member kind (<see cref="IsProperty"/>), and accessibility
/// (<see cref="AccessKeyword"/> / <see cref="ExpectedAccessibility"/>), together with a pack
/// length <see cref="PackLength"/> and the element-type pool indices
/// (<see cref="ElementIndices"/>) used to bind the pack to a concrete <c>Type[]</c> of that
/// length when materializing the projections.
/// </summary>
public sealed record SpreadMemberScenario(
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
/// A generated correlated-spread scenario: two spreads (<see cref="StemA"/>,
/// <see cref="StemB"/>, guaranteed distinct and non-prefixing) of the <b>same</b> pack in one
/// type, together with the member kind, accessibility, and a pack length / element indices for
/// binding. Exercises Requirement 10.8 (two spreads of the same pack correlate at the same N).
/// </summary>
public sealed record CorrelatedSpreadScenario(
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
/// Property-based test for <b>Property 6 (Generated-member count equals pack length)</b>.
/// Builds the program model in-process via <see cref="ProgramModelBuilder"/> to read the real
/// <see cref="SpreadMember"/> family metadata, then drives the model's <see cref="TypeInterner"/>
/// to bind the pack to a concrete <c>Type[]</c> of length <c>N</c> and materialize the element
/// projections through the aggregate tuple's positional fields (the documented binding path,
/// since per-arity named-projection materialization is deferred to backend monomorphization,
/// task 14.1). Zero external process spawns.
/// <b>Validates: Requirements 10.1, 10.6, 10.7, 10.8.</b>
/// </summary>
public sealed class SpreadMemberCountPropertyTests : IDisposable
{
    private const int PoolSize = 6;
    private const int MaxPackLength = 6;
    private const string HolderTypeName = "Holder";
    private const string SpreadStemCollisionId = "MIST_VAR_0007";
    private const string EmptySpreadStemId = "MIST_VAR_0008";

    // Stems for the single-spread scenarios. Any one is safe as the sole member (its stem
    // namespace reserves Stem and Stem{i}, and there is no other member to collide with).
    private static readonly string[] SingleStems =
        ["Item", "Slot", "Value", "Elem", "Cell", "Node"];

    // Stems for correlated-spread scenarios: pairwise non-prefixing so that any two distinct
    // choices never trip the stem-collision diagnostic MIST_VAR_0007.
    private static readonly string[] CorrelatedStems =
        ["Key", "Value", "Alpha", "Beta", "Gamma", "Left", "Right", "Head"];

    // Accessibility keyword paired with the Accessibility the program model must record for it.
    private static readonly (string Keyword, Accessibility Access)[] AccessChoices =
    [
        ("public", Accessibility.Public),
        ("private", Accessibility.Private),
        ("internal", Accessibility.Internal),
        ("protected", Accessibility.Protected),
    ];

    private readonly string outputDir;

    public SpreadMemberCountPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsSpreadCount_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the temporary build directory.
            }
        }

        GC.SuppressFinalize(this);
    }

    // ─── FsCheck Arbitrary registrations ─────────────────────────────────────

    /// <summary>Generator for single-spread scenarios (stem, kind, accessibility, N, elements).</summary>
    public static Arbitrary<SpreadMemberScenario> SpreadScenarios()
    {
        var genElementIndex = Gen.Choose(0, PoolSize - 1);

        var gen =
            from stem in Gen.Elements(SingleStems)
            from isProperty in Gen.Elements(true, false)
            from access in Gen.Elements(AccessChoices)
            from n in Gen.Choose(0, MaxPackLength)
            from indices in Gen.ArrayOf(genElementIndex, n)
            select new SpreadMemberScenario(stem, isProperty, access.Keyword, access.Access, n, indices);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generator for correlated-spread scenarios. Picks two distinct indices into
    /// <see cref="CorrelatedStems"/> (via a non-zero offset modulo the pool size) so the two
    /// stems are always distinct and, because the pool is pairwise non-prefixing, never collide.
    /// </summary>
    public static Arbitrary<CorrelatedSpreadScenario> CorrelatedScenarios()
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
            select new CorrelatedSpreadScenario(
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
    // Property 6: for a member spread with stem S over a pack of length N, the family
    // records S/kind/accessibility, the aggregate is (TArgs...), and binding the pack to a
    // concrete Type[] of length N yields exactly N element projections whose types are the
    // pack elements in order (N=0 -> zero; N=1 -> one).
    //
    // **Validates: Requirements 10.1, 10.6, 10.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(SpreadMemberCountPropertyTests) }, MaxTest = 100)]
    public bool GeneratedMemberCount_EqualsPackLength(SpreadMemberScenario scenario)
    {
        var (model, diagnostics) = Build(BuildSingleSource(scenario));

        // A well-formed lone spread must not trip either spread-family diagnostic.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadStemCollisionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == EmptySpreadStemId);

        var holder = FindConcreteType(model, HolderTypeName);
        var pack = Assert.Single(holder.TypeParametersList);
        Assert.True(pack.IsPack);

        // ── Requirement 10.1: the family records the stem, member kind, and accessibility. ──
        var family = Assert.Single(holder.SpreadMembersList);
        Assert.Equal(scenario.Stem, family.Stem);
        Assert.Equal(
            scenario.IsProperty ? SpreadMemberKind.Property : SpreadMemberKind.Field,
            family.Kind);
        Assert.Equal(scenario.ExpectedAccessibility, family.Modifiers.Accessibility);
        Assert.Same(pack, family.Pack);

        // The aggregate member S itself is surfaced with the tuple type and the declaration's
        // accessibility and member kind (Requirement 10.1).
        AssertAggregateMember(holder, scenario, family);

        // ── The aggregate is (TArgs...): the interned 1-element tuple over the symbolic pack. ──
        var interner = InternerOf(model);
        Assert.Same(family.AggregateType, interner.GetOrCreateTupleType(new IMistType[] { pack }));
        var symbolicElement = Assert.Single(family.AggregateType.ElementTypes);
        Assert.Same(pack, symbolicElement);

        // ── Bind the pack to a concrete Type[] of length N and materialize the projections. ──
        // Binding re-interns the aggregate into the N-element tuple whose positional fields
        // Item1..ItemN back the element projections Stem1..StemN (SpreadMember contract).
        var elements = BuildElements(scenario.ElementIndices);
        var boundAggregate = interner.GetOrCreateTupleType(elements);

        // Exactly N projections: empty pack -> zero (R10.6); length-1 -> one (R10.7).
        Assert.Equal(scenario.PackLength, boundAggregate.PositionalFields.Count);
        Assert.Equal(scenario.PackLength, boundAggregate.ElementTypes.Count);

        for(var k = 1; k <= scenario.PackLength; k++)
        {
            var projection = boundAggregate.PositionalFields[k - 1];

            // Projection Sk has the pack element type at 0-based index k-1 and equals the
            // k-th element of the aggregate S.
            Assert.Same(elements[k - 1], projection.Type);
            Assert.Same(boundAggregate.ElementTypes[k - 1], projection.Type);

            // The positional backing name is Item{k} (ValueTuple naming); Sk surfaces it
            // under the stem once the named projections are materialized (task 14.1).
            Assert.Equal(TupleType.PositionalName(k), projection.Name);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6 (correlation): two spreads of the SAME pack produce two families over one
    // shared pack, so they bind identically and their projections align at the same N.
    //
    // **Validates: Requirement 10.8**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(SpreadMemberCountPropertyTests) }, MaxTest = 100)]
    public bool CorrelatedSpreads_ShareOnePackAndAlignAtN(CorrelatedSpreadScenario scenario)
    {
        var (model, diagnostics) = Build(BuildCorrelatedSource(scenario));

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadStemCollisionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == EmptySpreadStemId);

        var holder = FindConcreteType(model, HolderTypeName);
        var pack = Assert.Single(holder.TypeParametersList);

        // Requirement 10.8: two spreads -> two families, both over the SAME pack instance.
        Assert.Equal(2, holder.SpreadMembersList.Count);
        var famA = holder.SpreadMembersList.Single(s => s.Stem == scenario.StemA);
        var famB = holder.SpreadMembersList.Single(s => s.Stem == scenario.StemB);
        Assert.Same(pack, famA.Pack);
        Assert.Same(pack, famB.Pack);

        // Because they share the pack, they share the single interned aggregate (TArgs...),
        // so binding produces one N-element tuple that both spreads project index-for-index.
        Assert.Same(famA.AggregateType, famB.AggregateType);

        var interner = InternerOf(model);
        var elements = BuildElements(scenario.ElementIndices);
        var bound = interner.GetOrCreateTupleType(elements);

        // Both stems project exactly N index-aligned element types (Stem A_1..A_N and
        // Stem B_1..B_N over the same element at each index); the two families are distinctly
        // named (distinct stems), so their reserved projection namespaces do not overlap.
        Assert.Equal(scenario.PackLength, bound.PositionalFields.Count);
        Assert.NotEqual(scenario.StemA, scenario.StemB);

        for(var k = 1; k <= scenario.PackLength; k++)
        {
            Assert.Same(elements[k - 1], bound.PositionalFields[k - 1].Type);
        }

        return true;
    }

    // ─── Assertion helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Asserts the real aggregate member <c>S</c> is surfaced as the declared member kind
    /// (field or property) with the aggregate tuple type and the declared accessibility, and
    /// that no member of the other kind is produced (Requirement 10.1).
    /// </summary>
    private static void AssertAggregateMember(ConcreteType holder, SpreadMemberScenario scenario, SpreadMember family)
    {
        if(scenario.IsProperty)
        {
            var aggregate = Assert.Single(holder.PropertiesList);
            Assert.Equal(scenario.Stem, aggregate.Name);
            Assert.Same(family.AggregateType, aggregate.Type);
            Assert.Equal(scenario.ExpectedAccessibility, aggregate.Modifiers.Accessibility);
            Assert.NotNull(aggregate.Getter);
            Assert.Empty(holder.FieldsList);
        }
        else
        {
            var aggregate = Assert.Single(holder.FieldsList);
            Assert.Equal(scenario.Stem, aggregate.Name);
            Assert.Same(family.AggregateType, aggregate.Type);
            Assert.Equal(scenario.ExpectedAccessibility, aggregate.Modifiers.Accessibility);
            Assert.Empty(holder.PropertiesList);
        }
    }

    // ─── Source construction ────────────────────────────────────────────────

    private static string BuildSingleSource(SpreadMemberScenario scenario)
    {
        var member = MemberLine(scenario.IsProperty, scenario.AccessKeyword, scenario.Stem);
        return $"public class {HolderTypeName}<params TArgs>\n{{\n{member}\n}}";
    }

    private static string BuildCorrelatedSource(CorrelatedSpreadScenario scenario)
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

    // ─── Element-type pool for binding the pack to a concrete Type[] ──────────

    private static IMistType[] BuildElements(IReadOnlyList<int> indices)
    {
        var pool = BuildElementPool();
        var result = new IMistType[indices.Count];

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
            new ClassType("Int32", "Standard", new Scope(null), null, null),
            new ClassType("String", "Standard", new Scope(null), null, null),
            new ClassType("Boolean", "Standard", new Scope(null), null, null),
            new ClassType("Double", "Standard", new Scope(null), null, null),
            new ClassType("Int64", "Standard", new Scope(null), null, null),
            new ClassType("Char", "Standard", new Scope(null), null, null),
        ];
    }

    // ─── In-process program-model build (zero process spawns) ─────────────────

    private (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) Build(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }

    private static TypeInterner InternerOf(IProgramModel model)
    {
        return ((ProgramModel.Model.ProgramModel)model).TypeInterner;
    }

    private static ConcreteType FindConcreteType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<ConcreteType>()
            .Where(t => t.Origin == TypeOrigin.Source && SimpleName(t.Name) == simpleName)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one source type named '{simpleName}', found {matches.Count}.");
        return matches[0];
    }

    private static string SimpleName(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }
}