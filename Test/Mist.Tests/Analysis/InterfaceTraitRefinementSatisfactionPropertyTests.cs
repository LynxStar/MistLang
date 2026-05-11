// Feature: adhoc-refinement-constraints, Property 9: Interface/trait refinement constraint satisfaction
// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.8, 17.9**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for interface/trait method refinement constraint satisfaction
/// (Liskov Substitution Principle).
///
/// Property 9: For any interface or trait method with a parameter refinement constraint,
/// and any implementing method on a class or struct, the type checker SHALL accept the
/// implementation if and only if, for each parameter position where the interface/trait
/// declares a refinement predicate, either (a) the implementation declares no refinement
/// constraint on that parameter, or (b) RefinementSolver.Implies(interfacePredicate,
/// implPredicate) returns true. If the implication does not hold (the implementation's
/// constraint is stronger), the type checker SHALL emit a MIST_REFINEMENT_0009 diagnostic
/// for that parameter. Each parameter position is validated independently.
///
/// Uses the full compilation pipeline (lexer → parser → semantic model → type checker) via
/// a shared CompilerDriver instance to comply with the test-process-spawning policy.
///
/// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.8, 17.9**
/// </summary>
public class InterfaceTraitRefinementSatisfactionPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Determines whether interfacePredicate implies implPredicate using the same
    /// algebraic (real-number) semantics as <see cref="RefinementSolver"/>. This avoids
    /// false positives from integer brute-force (e.g., &lt; 49 does NOT imply &lt;= 48
    /// under real-number semantics because 48.5 satisfies &lt; 49 but not &lt;= 48).
    /// </summary>
    private static bool AlgebraicImplies(string ifaceOp, int ifaceThreshold, string implOp, int implThreshold)
    {
        // Mirrors RefinementPredicate.ConstraintImplies logic exactly.
        return (ifaceOp, implOp) switch
        {
            // Source: >=
            (">=", ">=") => ifaceThreshold >= implThreshold,
            (">=", ">") => ifaceThreshold > implThreshold,

            // Source: >
            (">", ">=") => ifaceThreshold >= implThreshold,
            (">", ">") => ifaceThreshold >= implThreshold,

            // Source: <=
            ("<=", "<=") => ifaceThreshold <= implThreshold,
            ("<=", "<") => ifaceThreshold < implThreshold,

            // Source: <
            ("<", "<=") => ifaceThreshold <= implThreshold,
            ("<", "<") => ifaceThreshold <= implThreshold,

            _ => false
        };
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// The kind of constraint relationship between interface and implementation.
    /// </summary>
    public enum ConstraintRelation
    {
        /// <summary>Implementation has the same predicate as the interface.</summary>
        SamePredicate,

        /// <summary>Implementation has no constraint (accepts all values).</summary>
        NoPredicate,

        /// <summary>Implementation has a weaker predicate (implication holds).</summary>
        WeakerPredicate,

        /// <summary>Implementation has a stronger predicate (implication fails).</summary>
        StrongerPredicate,

        /// <summary>Interface has no constraint (implementation may add any).</summary>
        InterfaceNoConstraint
    }

    /// <summary>
    /// Whether the contract is declared as an interface or a trait.
    /// Note: TraitMethodDeclaration in the Mist grammar does not support
    /// constraints:WhereClause*, so trait methods cannot have refinement
    /// constraints. For traits, the test verifies that implementations
    /// can freely add any constraint (Requirement 17.8 applies).
    /// </summary>
    public enum ContractKind
    {
        Interface,
        Trait
    }

    /// <summary>
    /// Scenario for interface/trait refinement constraint satisfaction testing.
    /// Covers method parameters on both interfaces and traits.
    /// Note: Interfaces and traits in Mist do not support indexer declarations
    /// in the grammar, so indexer parameter refinement is tested separately
    /// on class-level indexers in other property tests.
    /// </summary>
    public sealed class SatisfactionScenario
    {
        public ContractKind Contract { get; }
        public ConstraintRelation Relation { get; }
        public string InterfaceOp { get; }
        public int InterfaceThreshold { get; }
        public string ImplOp { get; }
        public int ImplThreshold { get; }

        public SatisfactionScenario(
            ContractKind contract,
            ConstraintRelation relation,
            string interfaceOp,
            int interfaceThreshold,
            string implOp,
            int implThreshold)
        {
            Contract = contract;
            Relation = relation;
            InterfaceOp = interfaceOp;
            InterfaceThreshold = interfaceThreshold;
            ImplOp = implOp;
            ImplThreshold = implThreshold;
        }

        /// <summary>
        /// Whether this scenario should be accepted (no MIST_REFINEMENT_0009 diagnostic).
        /// Cases (a) same predicate, (b) no predicate, (c) weaker predicate, and
        /// (e) interface has no constraint are all accepted.
        /// Case (d) stronger predicate is rejected.
        /// For traits: since TraitMethodDeclaration doesn't support WhereClause*,
        /// trait methods never have refinement constraints, so all trait scenarios
        /// are accepted (Requirement 17.8).
        /// </summary>
        public bool ShouldAccept => Contract == ContractKind.Trait
            || Relation != ConstraintRelation.StrongerPredicate;

        public string ToSource()
        {
            var contractKeyword = Contract == ContractKind.Interface ? "interface" : "trait";

            // Trait methods don't support constraints:WhereClause* in the grammar,
            // so we never emit a constraint on the trait method declaration.
            var ifaceConstraint = Contract == ContractKind.Trait
                ? ""
                : Relation == ConstraintRelation.InterfaceNoConstraint
                    ? ""
                    : $" where x {InterfaceOp} {InterfaceThreshold}";

            var implConstraint = Relation switch
            {
                ConstraintRelation.NoPredicate => "",
                _ => $" where x {ImplOp} {ImplThreshold}"
            };

            return $$"""
                namespace Test;
                {{contractKeyword}} IContract {
                    void Process(int x){{ifaceConstraint}};
                }
                class Impl : IContract {
                    public void Process(int x){{implConstraint}} { }
                }
                public class Program {
                    public static void Main() { }
                }
                """;
        }

        public override string ToString() =>
            $"{Contract} [{Relation}]: iface={InterfaceOp} {InterfaceThreshold}, impl={ImplOp} {ImplThreshold} → {(ShouldAccept ? "accept" : "reject")}";
    }

    /// <summary>
    /// Scenario for multi-parameter interface method refinement constraint satisfaction.
    /// Tests that each parameter is validated independently.
    /// </summary>
    public sealed class MultiParamSatisfactionScenario
    {
        public ContractKind Contract { get; }
        public string Iface1Op { get; }
        public int Iface1Threshold { get; }
        public string Iface2Op { get; }
        public int Iface2Threshold { get; }
        public string Impl1Op { get; }
        public int Impl1Threshold { get; }
        public string Impl2Op { get; }
        public int Impl2Threshold { get; }
        public bool Param1HasConstraint { get; }
        public bool Param2HasConstraint { get; }

        public MultiParamSatisfactionScenario(
            ContractKind contract,
            string iface1Op, int iface1Threshold,
            string iface2Op, int iface2Threshold,
            string impl1Op, int impl1Threshold,
            string impl2Op, int impl2Threshold,
            bool param1HasConstraint, bool param2HasConstraint)
        {
            Contract = contract;
            Iface1Op = iface1Op;
            Iface1Threshold = iface1Threshold;
            Iface2Op = iface2Op;
            Iface2Threshold = iface2Threshold;
            Impl1Op = impl1Op;
            Impl1Threshold = impl1Threshold;
            Impl2Op = impl2Op;
            Impl2Threshold = impl2Threshold;
            Param1HasConstraint = param1HasConstraint;
            Param2HasConstraint = param2HasConstraint;
        }

        /// <summary>
        /// Number of expected MIST_REFINEMENT_0009 diagnostics.
        /// Each parameter is validated independently.
        /// For traits: since TraitMethodDeclaration doesn't support WhereClause*,
        /// trait methods never have refinement constraints, so violations are never
        /// expected (Requirement 17.8 — implementation may freely add constraints).
        /// </summary>
        public int ExpectedViolationCount
        {
            get
            {
                // Traits can't have method refinement constraints in the grammar
                if (Contract == ContractKind.Trait)
                    return 0;

                var count = 0;
                // Param 1: interface always has constraint; impl may or may not
                if (Param1HasConstraint && !AlgebraicImplies(Iface1Op, Iface1Threshold, Impl1Op, Impl1Threshold))
                    count++;
                // Param 2: interface always has constraint; impl may or may not
                if (Param2HasConstraint && !AlgebraicImplies(Iface2Op, Iface2Threshold, Impl2Op, Impl2Threshold))
                    count++;
                return count;
            }
        }

        public string ToSource()
        {
            var contractKeyword = Contract == ContractKind.Interface ? "interface" : "trait";

            // Trait methods don't support constraints:WhereClause* in the grammar,
            // so we never emit constraints on the trait method declaration.
            var ifaceConstraints = Contract == ContractKind.Trait
                ? ""
                : $" where x {Iface1Op} {Iface1Threshold} where y {Iface2Op} {Iface2Threshold}";

            var implConstraintParts = new List<string>();
            if (Param1HasConstraint)
                implConstraintParts.Add($"where x {Impl1Op} {Impl1Threshold}");
            if (Param2HasConstraint)
                implConstraintParts.Add($"where y {Impl2Op} {Impl2Threshold}");
            var implConstraints = implConstraintParts.Count > 0
                ? " " + string.Join(" ", implConstraintParts)
                : "";

            return $$"""
                namespace Test;
                {{contractKeyword}} IContract {
                    void Process(int x, int y){{ifaceConstraints}};
                }
                class Impl : IContract {
                    public void Process(int x, int y){{implConstraints}} { }
                }
                public class Program {
                    public static void Main() { }
                }
                """;
        }

        public override string ToString() =>
            $"{Contract} MultiParam: iface=({Iface1Op} {Iface1Threshold}, {Iface2Op} {Iface2Threshold}), " +
            $"impl=({(Param1HasConstraint ? $"{Impl1Op} {Impl1Threshold}" : "none")}, " +
            $"{(Param2HasConstraint ? $"{Impl2Op} {Impl2Threshold}" : "none")}) → expect {ExpectedViolationCount} violations";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] Operators = [">=", "<=", ">", "<"];

    /// <summary>
    /// Generates a scenario where the implementation has the same predicate as the interface.
    /// Should always be accepted.
    /// </summary>
    private static Gen<SatisfactionScenario> GenSamePredicate()
    {
        return from contract in Gen.Elements(ContractKind.Interface, ContractKind.Trait)
               from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               select new SatisfactionScenario(
                   contract, ConstraintRelation.SamePredicate,
                   op, threshold, op, threshold);
    }

    /// <summary>
    /// Generates a scenario where the implementation has no constraint.
    /// Should always be accepted (no constraint means accepts all values).
    /// </summary>
    private static Gen<SatisfactionScenario> GenNoPredicate()
    {
        return from contract in Gen.Elements(ContractKind.Interface, ContractKind.Trait)
               from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               select new SatisfactionScenario(
                   contract, ConstraintRelation.NoPredicate,
                   op, threshold, op, threshold); // implOp/implThreshold unused when NoPredicate
    }

    /// <summary>
    /// Generates a scenario where the implementation has a weaker predicate
    /// (implication holds: interface predicate implies impl predicate).
    /// Should always be accepted.
    /// </summary>
    private static Gen<SatisfactionScenario> GenWeakerPredicate()
    {
        return from contract in Gen.Elements(ContractKind.Interface, ContractKind.Trait)
               from ifaceOp in Gen.Elements(Operators)
               from ifaceThreshold in Gen.Choose(0, 100)
               from implOp in Gen.Elements(Operators)
               from implThreshold in Gen.Choose(0, 100)
               where AlgebraicImplies(ifaceOp, ifaceThreshold, implOp, implThreshold)
                     && !(ifaceOp == implOp && ifaceThreshold == implThreshold) // exclude same predicate
               select new SatisfactionScenario(
                   contract, ConstraintRelation.WeakerPredicate,
                   ifaceOp, ifaceThreshold, implOp, implThreshold);
    }

    /// <summary>
    /// Generates a scenario where the implementation has a stronger predicate
    /// (implication fails: interface predicate does NOT imply impl predicate).
    /// Should always be rejected with MIST_REFINEMENT_0009.
    /// </summary>
    private static Gen<SatisfactionScenario> GenStrongerPredicate()
    {
        return from contract in Gen.Elements(ContractKind.Interface, ContractKind.Trait)
               from ifaceOp in Gen.Elements(Operators)
               from ifaceThreshold in Gen.Choose(0, 100)
               from implOp in Gen.Elements(Operators)
               from implThreshold in Gen.Choose(0, 100)
               where !AlgebraicImplies(ifaceOp, ifaceThreshold, implOp, implThreshold)
               select new SatisfactionScenario(
                   contract, ConstraintRelation.StrongerPredicate,
                   ifaceOp, ifaceThreshold, implOp, implThreshold);
    }

    /// <summary>
    /// Generates a scenario where the interface has no constraint.
    /// Implementation may freely add any constraint — should always be accepted.
    /// </summary>
    private static Gen<SatisfactionScenario> GenInterfaceNoConstraint()
    {
        return from contract in Gen.Elements(ContractKind.Interface, ContractKind.Trait)
               from implOp in Gen.Elements(Operators)
               from implThreshold in Gen.Choose(0, 100)
               select new SatisfactionScenario(
                   contract, ConstraintRelation.InterfaceNoConstraint,
                   ">=", 0, implOp, implThreshold); // ifaceOp/ifaceThreshold unused
    }

    private static Gen<SatisfactionScenario> GenSatisfactionScenario()
    {
        return Gen.OneOf(
            GenSamePredicate(),
            GenNoPredicate(),
            GenWeakerPredicate(),
            GenStrongerPredicate(),
            GenInterfaceNoConstraint());
    }

    /// <summary>
    /// Generates multi-parameter scenarios where both interface parameters have constraints
    /// and the implementation may or may not have constraints on each.
    /// </summary>
    private static Gen<MultiParamSatisfactionScenario> GenMultiParamScenario()
    {
        return from contract in Gen.Elements(ContractKind.Interface, ContractKind.Trait)
               from iface1Op in Gen.Elements(Operators)
               from iface1Threshold in Gen.Choose(0, 50)
               from iface2Op in Gen.Elements(Operators)
               from iface2Threshold in Gen.Choose(0, 50)
               from impl1Op in Gen.Elements(Operators)
               from impl1Threshold in Gen.Choose(0, 50)
               from impl2Op in Gen.Elements(Operators)
               from impl2Threshold in Gen.Choose(0, 50)
               from param1Has in Gen.Elements(true, false)
               from param2Has in Gen.Elements(true, false)
               where param1Has || param2Has // at least one impl param has a constraint
               select new MultiParamSatisfactionScenario(
                   contract,
                   iface1Op, iface1Threshold,
                   iface2Op, iface2Threshold,
                   impl1Op, impl1Threshold,
                   impl2Op, impl2Threshold,
                   param1Has, param2Has);
    }

    public static Arbitrary<SatisfactionScenario> ArbitrarySatisfactionScenario() =>
        GenSatisfactionScenario().ToArbitrary();

    public static Arbitrary<MultiParamSatisfactionScenario> ArbitraryMultiParamScenario() =>
        GenMultiParamScenario().ToArbitrary();

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 9a: Single-parameter interface/trait refinement constraint satisfaction.
    ///
    /// For any interface or trait method with a parameter refinement constraint, and any
    /// implementing method:
    /// - Same predicate → accepted (Requirement 17.1)
    /// - No predicate → accepted (Requirement 17.2)
    /// - Weaker predicate (implication holds) → accepted (Requirement 17.3)
    /// - Stronger predicate (implication fails) → rejected with MIST_REFINEMENT_0009 (Requirement 17.4)
    /// - Interface has no constraint → accepted regardless of impl constraint (Requirement 17.8)
    ///
    /// Covers both interfaces and traits (Requirement 17.6).
    ///
    /// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7, 17.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(InterfaceTraitRefinementSatisfactionPropertyTests) })]
    public Property SingleParam_AcceptedIffImplicationHolds(SatisfactionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSource());

        var violations = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.RefinementConstraintStrongerThanInterface)
            .ToList();

        var passed = scenario.ShouldAccept
            ? violations.Count == 0
            : violations.Count == 1;

        return passed.ToProperty()
            .Label($"{scenario}. Violations found: {violations.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 9b: Multi-parameter interface/trait refinement constraint satisfaction.
    ///
    /// For any interface or trait method with refinement constraints on multiple parameters,
    /// each parameter is validated independently — a violation on one parameter does not
    /// prevent validation of others.
    ///
    /// **Validates: Requirements 17.7, 17.9**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(InterfaceTraitRefinementSatisfactionPropertyTests) })]
    public Property MultiParam_EachParameterValidatedIndependently(MultiParamSatisfactionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSource());

        var violations = diagnostics
            .Where(d => d.Id == SemanticModelBuilder.RefinementConstraintStrongerThanInterface)
            .ToList();

        var passed = violations.Count == scenario.ExpectedViolationCount;

        return passed.ToProperty()
            .Label($"{scenario}. Expected {scenario.ExpectedViolationCount} violations, got {violations.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
