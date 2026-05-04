// Feature: unknown-type (Phase C, Task 19.2)
// **Validates: Requirement 9.10**
//
// Property 4: Fixed-point iteration is monotone.
//
// The claim: for any synthetic call graph and seed classification, running
// ReachabilityAnalyzer.RunFixedPointStep zero or more times produces a
// sequence of classification maps M0, M1, M2, ..., Mk (where Mk = Mk+1 =
// "the fixed point"). Monotonicity says that for every key, if Mi[key] =
// WritesT then Mi+1[key] = WritesT (no entry ever downgrades), and more
// strongly: for every i < j, Mi[key] <= Mj[key] in the lattice order
// DoesNotWriteT < WritesT.
//
// The test runs entirely in-process: we build synthetic call graphs from
// scratch (no compilation, no process spawning) and run the analyzer's
// step function directly.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Storm.Runtime;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based test for fixed-point monotonicity (Task 19.2).
///
/// <para>
/// <strong>Property 4 (unknown-type feature):</strong> Fixed-point
/// iteration is monotone. For any iteration <c>k</c>, the classification
/// map is a pointwise under-approximation of the final map — no entry ever
/// changes from <see cref="MemberClassification.WritesT"/> back to
/// <see cref="MemberClassification.DoesNotWriteT"/>.
/// <strong>Validates: Requirement 9.10</strong>
/// </para>
/// </summary>
public class ReachabilityAnalyzerFixedPointPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Synthetic model builders — in-process IType / ISymbol construction
    // for property-based generation. No compilation, no file I/O.
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class SentinelNode : SyntaxNode
    {
        public override IEnumerable<SyntaxNode> Children() => Array.Empty<SyntaxNode>();
    }

    /// <summary>
    /// A synthetic call graph describing N methods on a single generic type
    /// <c>G&lt;T&gt;</c>. Each method is either:
    /// <list type="bullet">
    /// <item><description>
    /// <c>Seed = WritesT</c> — the intrinsic Task-18 classification decided
    /// the method writes T (e.g., direct write-site), and the fixed-point
    /// loop cannot change it.
    /// </description></item>
    /// <item><description>
    /// <c>Seed = DoesNotWriteT</c> with a list of internal callee indices —
    /// the method becomes WritesT iff any callee is transitively WritesT.
    /// </description></item>
    /// </list>
    /// The scenario is deliberately small (≤ 6 methods) so FsCheck can
    /// explore a wide variety of graph shapes quickly. Edges are encoded
    /// as indices rather than symbol references so the generator is pure.
    /// </summary>
#pragma warning disable CA1034 // Nested types — kept nested for scenario locality in tests.
#pragma warning disable CA1819 // Test scenarios use arrays for FsCheck interop; no external callers mutate.
    public sealed class CallGraphScenario
    {
        /// <summary>
        /// Per-method seed classifications. Length is the number of methods.
        /// </summary>
        public bool[] SeedWritesT { get; }

        /// <summary>
        /// Per-method internal-callee index list. <c>InternalCallees[i]</c>
        /// is the set of method indices that method <c>i</c> calls via an
        /// internal (same-type) dispatch, recorded as
        /// <see cref="MemberClassification"/> propagation edges.
        /// </summary>
        public int[][] InternalCallees { get; }

        public CallGraphScenario(bool[] seedWritesT, int[][] internalCallees)
        {
            SeedWritesT = seedWritesT;
            InternalCallees = internalCallees;
        }

        public int MethodCount => SeedWritesT.Length;

        public override string ToString()
        {
            var edges = string.Join("; ",
                Enumerable.Range(0, MethodCount)
                    .Select(i => $"{i}[{(SeedWritesT[i] ? "W" : "_")}]→{{{string.Join(",", InternalCallees[i])}}}"));
            return $"[{MethodCount} methods] {edges}";
        }
    }
#pragma warning restore CA1819
#pragma warning restore CA1034

    /// <summary>
    /// FsCheck Arbitrary for <see cref="CallGraphScenario"/>. Produces
    /// graphs of 1–6 methods with a random subset marked <c>WritesT</c>
    /// and random internal-callee edges (including self-loops and
    /// mutual recursion).
    /// </summary>
    public static Arbitrary<CallGraphScenario> ArbitraryCallGraphScenario()
    {
        var gen =
            from n in Gen.Choose(1, 6)
            from seeds in Gen.ArrayOf(Gen.Elements(true, false), n)
            from edges in GenEdgeLists(n)
            select new CallGraphScenario(seeds, edges);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates an array of N callee-index arrays. For method <c>i</c>,
    /// the generator picks a count <c>k ∈ [0, n]</c> and then samples
    /// <c>k</c> callee indices from <c>[0, n)</c>. Duplicate callees
    /// are collapsed; self-loops are intentionally allowed.
    /// </summary>
    private static Gen<int[][]> GenEdgeLists(int n)
    {
        if (n == 0)
            return Gen.Constant(Array.Empty<int[]>());

        var perMethod =
            from k in Gen.Choose(0, n)
            from callees in Gen.ArrayOf(Gen.Choose(0, n - 1), k)
            select callees.Distinct().ToArray();

        return Gen.ArrayOf(perMethod, n);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Synthetic ISymbol/IType construction for a single generic type G<T>.
    // ═══════════════════════════════════════════════════════════════════════

    private static (List<ISymbol> methods, TypeParameterType T, Dictionary<ISymbol, ReachabilityAnalyzer.MemberCallInfo> callGraph, Dictionary<ReachabilityAnalyzer.MemberTypeParamKey, MemberClassification> classifications, List<(List<ISymbol> Members, IReadOnlyList<TypeParameterType> TypeParameters)> perGeneric)
        BuildSyntheticWorld(CallGraphScenario scenario)
    {
        var fileScope = new Scope(parent: null);
        var memberScope = new Scope(parent: fileScope);
        var typeParameter = new TypeParameterType("T", 0);

        // Create N method symbols and register them in the member scope.
        var methods = new List<ISymbol>(scenario.MethodCount);
        for (var i = 0; i < scenario.MethodCount; i++)
        {
            var method = new Symbol(
                name: $"m{i}",
                kind: SymbolKind.Method,
                origin: SymbolOrigin.UserCode,
                containingScope: memberScope,
                declaredType: null,
                declarationSpan: null);
            memberScope.AddMember(method);
            methods.Add(method);
        }

        // Build the MemberCallInfo for each method using the scenario's
        // edge list. Internal call sites carry a resolved Callee symbol;
        // there are no write-sites and no external calls — the seed
        // classification (set below) is what drives the initial map.
        var callGraph = new Dictionary<ISymbol, ReachabilityAnalyzer.MemberCallInfo>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < scenario.MethodCount; i++)
        {
            var callSites = new List<ReachabilityAnalyzer.CallSite>();
            foreach (var calleeIndex in scenario.InternalCallees[i])
            {
                callSites.Add(new ReachabilityAnalyzer.CallSite(
                    Callee: methods[calleeIndex],
                    IsInternal: true,
                    IsDynamic: false,
                    ArgumentTypes: Array.Empty<IType>()));
            }
            callGraph[methods[i]] = new ReachabilityAnalyzer.MemberCallInfo(
                writeSiteTargetTypes: Array.Empty<IType>(),
                callSites: callSites);
        }

        // Seed the classification map with the scenario's intrinsic
        // WritesT/DoesNotWriteT decisions.
        var classifications = new Dictionary<ReachabilityAnalyzer.MemberTypeParamKey, MemberClassification>(
            ReachabilityAnalyzer.MemberTypeParamKey.Comparer);
        for (var i = 0; i < scenario.MethodCount; i++)
        {
            var key = new ReachabilityAnalyzer.MemberTypeParamKey(methods[i], typeParameter);
            classifications[key] = scenario.SeedWritesT[i]
                ? MemberClassification.WritesT
                : MemberClassification.DoesNotWriteT;
        }

        var perGeneric = new List<(List<ISymbol> Members, IReadOnlyList<TypeParameterType> TypeParameters)>
        {
            (methods, new[] { typeParameter }),
        };

        return (methods, typeParameter, callGraph, classifications, perGeneric);
    }

    /// <summary>
    /// Snapshots the classification state of every method in
    /// <paramref name="methods"/> against <paramref name="T"/>. Used to
    /// compare maps across iterations.
    /// </summary>
    private static MemberClassification[] Snapshot(
        Dictionary<ReachabilityAnalyzer.MemberTypeParamKey, MemberClassification> classifications,
        IReadOnlyList<ISymbol> methods,
        TypeParameterType T)
    {
        var result = new MemberClassification[methods.Count];
        for (var i = 0; i < methods.Count; i++)
        {
            var key = new ReachabilityAnalyzer.MemberTypeParamKey(methods[i], T);
            result[i] = classifications[key];
        }
        return result;
    }

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="before"/> is pointwise
    /// &lt;= <paramref name="after"/> in the lattice. In the two-point
    /// lattice, that means: wherever <paramref name="before"/> is
    /// <see cref="MemberClassification.WritesT"/>, <paramref name="after"/>
    /// must also be <see cref="MemberClassification.WritesT"/>.
    /// </summary>
    private static bool IsPointwiseLessOrEqual(
        MemberClassification[] before,
        MemberClassification[] after)
    {
        if (before.Length != after.Length)
            return false;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] == MemberClassification.WritesT
                && after[i] != MemberClassification.WritesT)
                return false;
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Fixed-point iteration is monotone.
    // **Validates: Requirement 9.10**
    //
    // Run the step function up to the natural fixed point and assert:
    //   1. Every snapshot is <= the next snapshot (local monotonicity).
    //   2. Every snapshot is <= the final snapshot (global monotonicity).
    //   3. The loop terminates (bounded by the lattice height × method
    //      count: |methods| iterations always suffice for height-2).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(ReachabilityAnalyzerFixedPointPropertyTests) },
        MaxTest = 200)]
    public bool FixedPointIteration_IsMonotonePointwise(CallGraphScenario scenario)
    {
        var (methods, T, callGraph, classifications, perGeneric) = BuildSyntheticWorld(scenario);

        // Capture a sequence of snapshots, stepping the fixed-point loop
        // manually. The loop terminates when the step function reports
        // no change.
        var snapshots = new List<MemberClassification[]>
        {
            Snapshot(classifications, methods, T),
        };

        // Hard upper bound: for a height-2 lattice, the number of
        // promotions is <= |methods|. Add 1 for the terminating "no
        // change" pass we must always do.
        var maxIterations = methods.Count + 2;
        for (var i = 0; i < maxIterations; i++)
        {
            var changed = ReachabilityAnalyzer.RunFixedPointStep(classifications, callGraph, perGeneric);
            snapshots.Add(Snapshot(classifications, methods, T));
            if (!changed)
                break;
        }

        // The final snapshot must equal the result of running the
        // convergence-based Run function on a fresh state; i.e., we
        // truly reached the fixed point. Build a fresh world so the
        // convergence-based run does not interfere with the step-based
        // snapshot sequence; then look up its state using ITS symbols
        // (not the original methods list).
        var (methods2, T2, callGraph2, classifications2, perGeneric2) = BuildSyntheticWorld(scenario);
        ReachabilityAnalyzer.RunFixedPoint(classifications2, callGraph2, perGeneric2);
        var finalSnapshot = Snapshot(classifications2, methods2, T2);

        // Local monotonicity: each step is pointwise <= the next.
        for (var i = 0; i + 1 < snapshots.Count; i++)
        {
            if (!IsPointwiseLessOrEqual(snapshots[i], snapshots[i + 1]))
                return false;
        }

        // Global monotonicity: every snapshot is <= the final fixed point.
        // (Uses an independently-built world whose step-based sequence
        // must agree pointwise with the convergence-based fixed point.)
        foreach (var snapshot in snapshots)
        {
            if (!IsPointwiseLessOrEqual(snapshot, finalSnapshot))
                return false;
        }

        // Termination: the step-based loop must have stopped changing by
        // the end (last iteration was a no-change pass, or we exhausted
        // maxIterations). The two runs must agree on the end state.
        var stepBasedFinal = snapshots[snapshots.Count - 1];
        for (var i = 0; i < stepBasedFinal.Length; i++)
        {
            if (stepBasedFinal[i] != finalSnapshot[i])
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Deterministic unit-test coverage of the same monotonicity property,
    // for the hand-crafted graphs called out in the task description.
    // These provide fast, human-readable regression coverage on top of
    // the randomized property test above.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleMember_DoesNotWriteT_StaysUnchanged()
    {
        var scenario = new CallGraphScenario(
            seedWritesT: new[] { false },
            internalCallees: new[] { Array.Empty<int>() });

        AssertFixedPointIsMonotone(scenario);
    }

    [Fact]
    public void SingleMember_WritesT_StaysWritesT()
    {
        var scenario = new CallGraphScenario(
            seedWritesT: new[] { true },
            internalCallees: new[] { Array.Empty<int>() });

        AssertFixedPointIsMonotone(scenario);
    }

    [Fact]
    public void TwoMembers_MutuallyReferencing_OneSeedWritesT_BothEndWritesT()
    {
        // A → B, B → A; A is seeded WritesT.
        var scenario = new CallGraphScenario(
            seedWritesT: new[] { true, false },
            internalCallees: new[] { new[] { 1 }, new[] { 0 } });

        AssertFixedPointIsMonotone(scenario);

        // And verify the actual final state.
        var (methods, T, callGraph, classifications, perGeneric) = BuildSyntheticWorld(scenario);
        ReachabilityAnalyzer.RunFixedPoint(classifications, callGraph, perGeneric);
        var final = Snapshot(classifications, methods, T);
        Assert.Equal(MemberClassification.WritesT, final[0]);
        Assert.Equal(MemberClassification.WritesT, final[1]);
    }

    [Fact]
    public void Chain_A_To_B_To_C_To_D_LastWritesT_AllBecomeWritesT()
    {
        // A → B → C → D; D is seeded WritesT. All four end WritesT.
        var scenario = new CallGraphScenario(
            seedWritesT: new[] { false, false, false, true },
            internalCallees: new[]
            {
                new[] { 1 },
                new[] { 2 },
                new[] { 3 },
                Array.Empty<int>(),
            });

        AssertFixedPointIsMonotone(scenario);

        var (methods, T, callGraph, classifications, perGeneric) = BuildSyntheticWorld(scenario);
        ReachabilityAnalyzer.RunFixedPoint(classifications, callGraph, perGeneric);
        var final = Snapshot(classifications, methods, T);
        Assert.All(final, c => Assert.Equal(MemberClassification.WritesT, c));
    }

    [Fact]
    public void NonPromotion_A_Calls_B_NeitherWritesT_BothStayDoesNotWriteT()
    {
        var scenario = new CallGraphScenario(
            seedWritesT: new[] { false, false },
            internalCallees: new[] { new[] { 1 }, Array.Empty<int>() });

        AssertFixedPointIsMonotone(scenario);

        var (methods, T, callGraph, classifications, perGeneric) = BuildSyntheticWorld(scenario);
        ReachabilityAnalyzer.RunFixedPoint(classifications, callGraph, perGeneric);
        var final = Snapshot(classifications, methods, T);
        Assert.Equal(MemberClassification.DoesNotWriteT, final[0]);
        Assert.Equal(MemberClassification.DoesNotWriteT, final[1]);
    }

    /// <summary>
    /// Helper that runs the monotonicity property check on a hand-built
    /// scenario and throws via <see cref="Assert.True(bool)"/> when it
    /// fails. Used by the deterministic [Fact] cases above.
    /// </summary>
    private static void AssertFixedPointIsMonotone(CallGraphScenario scenario)
    {
        var test = new ReachabilityAnalyzerFixedPointPropertyTests();
        Assert.True(
            test.FixedPointIteration_IsMonotonePointwise(scenario),
            $"Monotonicity property failed for scenario: {scenario}");
    }
}
