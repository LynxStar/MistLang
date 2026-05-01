// Task 17 of the Mist Standard Library spec: property-based test that
// operator overload resolution is deterministic under permutation of the
// candidate list. Design § *Correctness Properties → Property 4*:
//
//   For any well-typed operator expression and any ordering of the candidate
//   operator-member list provided to the type checker, repeated resolutions
//   select the same operator symbol every time.
//
// **Validates: Requirements 12.5, 12.8**

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for Task 17 of the Mist Standard Library spec:
/// <see cref="OverloadResolver.Resolve"/> is deterministic under any
/// permutation of its input candidate list, given a parallel origin vector
/// that tracks each candidate's operand-side regardless of list position.
///
/// Design § *Correctness Properties → Property 4*.
///
/// Approach (design note — "Approach B" in the task prompt):
/// a single fixed Mist fixture declares four structs <c>A</c>, <c>B</c>,
/// <c>C</c>, <c>D</c>, each with a <c>public static A operator +(A x, B y)</c>.
/// All four operator signatures are identical <c>(A, B)</c>, so
/// <c>MoreSpecific</c> yields no winner between any pair — every comparison
/// falls through to the origin-based tiebreaker. That's exactly the regime
/// Requirement 12.5 cares about: ties must resolve the same way no matter
/// how the candidate list is ordered.
///
/// Per iteration the property test:
/// <list type="number">
///   <item>Picks an arbitrary non-empty subset of the four operators as the
///         candidate set (size 2–4; single-candidate sets trivially
///         commute).</item>
///   <item>Picks an arbitrary origin (0 or 1) for each candidate.</item>
///   <item>Picks an arbitrary permutation of the (candidate, origin) pairs
///         — origin values follow their candidates so the tiebreaker sees
///         the same symbol-to-origin binding in both orderings.</item>
///   <item>Runs <c>Resolve</c> on both orderings and asserts the winners
///         match (both <c>Success</c> with the same <c>BestCandidate</c>, or
///         both <c>AmbiguousCall</c>).</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class OverloadResolverDeterminismPropertyTests
{
    // ─── Pipeline wiring (mirrors OverloadResolverOriginTiebreakerTests) ──

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Shared fixture ───────────────────────────────────────────────────

    /// <summary>
    /// The fixture is built once per test-class instance. The four operators
    /// and their declaring types are all we need across iterations — the
    /// generator picks subsets/permutations of this fixed universe.
    /// </summary>
    private readonly FixtureData _fixture;

    public OverloadResolverDeterminismPropertyTests()
    {
        _fixture = BuildFixture();
    }

    /// <summary>
    /// Holds the semantic model plus the four operator symbols and the
    /// operand types they all accept. <c>Operators</c> is ordered A, B, C, D
    /// so indices 0–3 map deterministically to the four operators.
    /// </summary>
    private sealed class FixtureData
    {
        public required ISemanticModel Model { get; init; }
        public required IReadOnlyList<ISymbol> Operators { get; init; }
        public required IType AType { get; init; }
        public required IType BType { get; init; }
    }

    /// <summary>
    /// Builds the four-struct fixture. Each struct declares the same
    /// <c>public static A operator +(A x, B y)</c> signature so no
    /// specificity difference exists between any pair — every resolution
    /// against this candidate set falls through to the origin-based
    /// tiebreaker, which is the regime Property 4 pins down.
    /// </summary>
    private static FixtureData BuildFixture()
    {
        // All four operators have signature (A, B). The return type is fixed
        // to A for simplicity; neither the return type nor the operator's
        // declaring struct affect MoreSpecific or applicability in this test.
        const string source =
            "public struct A { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "} " +
            "public struct B { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "} " +
            "public struct C { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "} " +
            "public struct D { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "}";

        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Fixture lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Fixture parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors,
            "Fixture semantic model build should not produce errors");

        // Pull the four struct types out by name. Ordering the operator list
        // as [A, B, C, D] gives the generator a stable universe to index
        // into.
        var typesByName = new[] { "A", "B", "C", "D" }
            .ToDictionary(
                name => name,
                name => (StructType)model.AllSymbols
                    .First(s => s.Name == name && s.Kind == SymbolKind.Struct)
                    .DeclaredType!);

        var operators = new[] { "A", "B", "C", "D" }
            .Select(name => typesByName[name].MemberScope.Members
                .Single(m => m.Name == "operator+"))
            .ToList();

        // Sanity: every operator has parameter signature (A, B). If the
        // fixture drifts — e.g., a rename or refactor changes the types
        // the operators consume — the determinism property would still
        // hold but the test would be exercising a weaker scenario, so we
        // pin the signatures here.
        var aType = typesByName["A"];
        var bType = typesByName["B"];
        foreach (var op in operators)
        {
            var paramTypes = ParameterSignature.Extract(op, model);
            Assert.Equal(2, paramTypes.Count);
            Assert.Same(aType, paramTypes[0]);
            Assert.Same(bType, paramTypes[1]);
        }

        return new FixtureData
        {
            Model = model,
            Operators = operators,
            AType = aType,
            BType = bType,
        };
    }

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// A single generated scenario: a subset of candidate indices (into the
    /// four fixture operators), an origin value for each selected candidate,
    /// and a permutation of those candidate positions.
    ///
    /// <c>CandidateIndices</c> has length N (2–4). <c>Origins</c> has the
    /// same length — <c>Origins[i]</c> is the origin for
    /// <c>CandidateIndices[i]</c>. <c>Permutation</c> is a rearrangement of
    /// <c>[0, N)</c>: the permuted candidate list is
    /// <c>[CandidateIndices[Permutation[0]], ...]</c> with origins bound
    /// to symbols the same way.
    /// </summary>
#pragma warning disable CA1034
    public record DeterminismScenario(
        IReadOnlyList<int> CandidateIndices,
        IReadOnlyList<int> Origins,
        IReadOnlyList<int> Permutation)
    {
        public override string ToString()
        {
            var cands = string.Join(",", CandidateIndices);
            var origins = string.Join(",", Origins);
            var perm = string.Join(",", Permutation);
            return $"Determinism: candidates=[{cands}] origins=[{origins}] permutation=[{perm}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="DeterminismScenario"/> instances. Strategy:
    /// <list type="number">
    ///   <item>Choose N in [2, 4] — the subset size (1 is trivially
    ///         deterministic and skipped).</item>
    ///   <item>Shuffle <c>[0, 1, 2, 3]</c> and take the first N entries as
    ///         the subset of fixture-operator indices.</item>
    ///   <item>Draw an origin (0 or 1) for each selected candidate
    ///         independently.</item>
    ///   <item>Shuffle <c>[0, N)</c> to produce the permutation.</item>
    /// </list>
    /// </summary>
    public static Arbitrary<DeterminismScenario> ArbitraryDeterminismScenarios()
    {
        var gen =
            from subsetSize in Gen.Choose(2, 4)
            from shuffledPool in Gen.Shuffle(new[] { 0, 1, 2, 3 })
            let candidateIndices = shuffledPool.Take(subsetSize).ToList()
            from origins in Gen.ArrayOf(Gen.Elements(0, 1), subsetSize)
            from permutation in Gen.Shuffle(Enumerable.Range(0, subsetSize).ToArray())
            select new DeterminismScenario(
                candidateIndices,
                origins.ToList(),
                permutation.ToList());

        return gen.ToArbitrary();
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 4: for any subset of operator candidates and any permutation
    /// of that subset (with origins bound to their candidates), resolving
    /// with both orderings selects the same winner — either both
    /// <c>Success</c> with the same <c>BestCandidate</c>, or both
    /// <c>AmbiguousCall</c>. Ambiguity arises here precisely when the
    /// minimum-origin bucket contains more than one candidate (see
    /// <c>OverloadResolver.MakeAmbiguousOrTiebreak</c>), and that shape is
    /// preserved by permutation, so this case must also commute.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 12.5, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OverloadResolverDeterminismPropertyTests) })]
    public bool OperatorOverloadResolution_IsDeterministicUnderPermutation(
        DeterminismScenario scenario)
    {
        // Build the original (candidate, origin) lists from the scenario.
        var originalCandidates = scenario.CandidateIndices
            .Select(i => _fixture.Operators[i])
            .ToList();
        var originalOrigins = scenario.Origins.ToList();
        var argumentTypes = new IType[] { _fixture.AType, _fixture.BType };

        // Apply the permutation, keeping each origin attached to its
        // candidate. This is the only thing that can "change" between
        // runs — we want to assert it changes nothing observable.
        var permutedCandidates = scenario.Permutation
            .Select(p => originalCandidates[p])
            .ToList();
        var permutedOrigins = scenario.Permutation
            .Select(p => originalOrigins[p])
            .ToList();

        var resultOriginal = OverloadResolver.Resolve(
            originalCandidates,
            argumentTypes,
            _fixture.Model,
            originalOrigins);

        var resultPermuted = OverloadResolver.Resolve(
            permutedCandidates,
            argumentTypes,
            _fixture.Model,
            permutedOrigins);

        // Both resolutions must agree on status.
        if (resultOriginal.Status != resultPermuted.Status)
            return false;

        // On Success, both must select the same symbol. ReferenceEquals is
        // appropriate: resolution returns whichever ISymbol instance is in
        // the candidate list, and permuting a list does not clone its
        // entries.
        if (resultOriginal.Status == OverloadResolutionStatus.Success)
        {
            return ReferenceEquals(
                resultOriginal.BestCandidate,
                resultPermuted.BestCandidate);
        }

        // Ambiguous and NoApplicableCandidate require only status equality
        // — any candidate-specific payload (e.g., AmbiguousCandidates list
        // order) is allowed to differ between permutations because list
        // order is not part of the "winner" identity Requirement 12.5
        // pins down.
        return true;
    }
}
