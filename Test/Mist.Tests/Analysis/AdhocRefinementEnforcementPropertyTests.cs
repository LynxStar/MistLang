// Feature: adhoc-refinement-constraints, Property 4: Compile-time acceptance of satisfying constants
// Feature: adhoc-refinement-constraints, Property 5: Compile-time rejection of violating constants
// **Validates: Requirements 5.1, 5.2, 6.1, 6.2, 6.5, 6.6, 6.7**

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
/// Property-based tests for compile-time enforcement of adhoc refinement constraints.
///
/// Property 4: For any refinement predicate and compile-time constant value that satisfies
/// the predicate, the type checker SHALL accept the construction (for dependent arguments)
/// or call (for parameter arguments) without emitting a diagnostic error.
///
/// Property 5: For any refinement predicate and compile-time constant value that does NOT
/// satisfy the predicate, the type checker SHALL emit a diagnostic error when the constant
/// is used as a dependent type argument or as a method/constructor/indexer argument.
///
/// Uses the full compilation pipeline (lexer → parser → semantic model → type checker) via
/// a shared CompilerDriver instance to comply with the test-process-spawning policy.
///
/// **Validates: Requirements 5.1, 5.2, 6.1, 6.2, 6.5, 6.6, 6.7**
/// </summary>
public class AdhocRefinementEnforcementPropertyTests
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
        _ = Directory.CreateDirectory(tempDir);
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
    /// Evaluates whether a value satisfies a relational predicate.
    /// </summary>
    private static bool Satisfies(int value, string op, int threshold)
    {
        return op switch
        {
            ">=" => value >= threshold,
            "<=" => value <= threshold,
            ">" => value > threshold,
            "<" => value < threshold,
            _ => false
        };
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Scenario for dependent type argument enforcement: a class with a dependent type
    /// parameter constrained by an adhoc refinement, constructed with a constant value.
    /// </summary>
    public sealed class DependentArgScenario
    {
        public string Operator { get; }
        public int Threshold { get; }
        public int ArgumentValue { get; }

        public DependentArgScenario(string op, int threshold, int argumentValue)
        {
            Operator = op;
            Threshold = threshold;
            ArgumentValue = argumentValue;
        }

        public bool ShouldAccept => Satisfies(ArgumentValue, Operator, Threshold);

        public string ToSource()
        {
            return $$"""
            class Container<Size:int> where Size {{Operator}} {{Threshold}} { }
            class Program {
                public static void Main() {
                    Container<{{ArgumentValue}}> c = new Container<{{ArgumentValue}}>();
                }
            }
            """;
        }

        public override string ToString()
        {
            return $"Container<Size:int> where Size {Operator} {Threshold}, arg={ArgumentValue} → {(ShouldAccept ? "accept" : "reject")}";
        }
    }

    /// <summary>
    /// Scenario for method parameter refinement enforcement: a method with a parameter
    /// constrained by an adhoc refinement, called with a constant value.
    /// </summary>
    public sealed class MethodParamScenario
    {
        public string Operator { get; }
        public int Threshold { get; }
        public int ArgumentValue { get; }

        public MethodParamScenario(string op, int threshold, int argumentValue)
        {
            Operator = op;
            Threshold = threshold;
            ArgumentValue = argumentValue;
        }

        public bool ShouldAccept => Satisfies(ArgumentValue, Operator, Threshold);

        public string ToSource()
        {
            return $$"""
            class Program {
                void Process(int value) where value {{Operator}} {{Threshold}} { }
                public static void Main() {
                    Program p = new Program();
                    p.Process({{ArgumentValue}});
                }
            }
            """;
        }

        public override string ToString()
        {
            return $"Process(int value) where value {Operator} {Threshold}, arg={ArgumentValue} → {(ShouldAccept ? "accept" : "reject")}";
        }
    }

    /// <summary>
    /// Scenario for constructor parameter refinement enforcement: a constructor with a
    /// parameter constrained by an adhoc refinement, called with a constant value.
    /// </summary>
    public sealed class ConstructorParamScenario
    {
        public string Operator { get; }
        public int Threshold { get; }
        public int ArgumentValue { get; }

        public ConstructorParamScenario(string op, int threshold, int argumentValue)
        {
            Operator = op;
            Threshold = threshold;
            ArgumentValue = argumentValue;
        }

        public bool ShouldAccept => Satisfies(ArgumentValue, Operator, Threshold);

        public string ToSource()
        {
            return $$"""
            class Widget {
                Widget(int size) where size {{Operator}} {{Threshold}} { }
            }
            class Program {
                public static void Main() {
                    Widget w = new Widget({{ArgumentValue}});
                }
            }
            """;
        }

        public override string ToString()
        {
            return $"Widget(int size) where size {Operator} {Threshold}, arg={ArgumentValue} → {(ShouldAccept ? "accept" : "reject")}";
        }
    }

    /// <summary>
    /// Scenario for indexer parameter refinement enforcement: an indexer with a parameter
    /// constrained by an adhoc refinement, called with a constant value.
    /// </summary>
    public sealed class IndexerParamScenario
    {
        public string Operator { get; }
        public int Threshold { get; }
        public int ArgumentValue { get; }

        public IndexerParamScenario(string op, int threshold, int argumentValue)
        {
            Operator = op;
            Threshold = threshold;
            ArgumentValue = argumentValue;
        }

        public bool ShouldAccept => Satisfies(ArgumentValue, Operator, Threshold);

        public string ToSource()
        {
            return $$"""
            class Collection {
                int this[int index] where index {{Operator}} {{Threshold}} { get { return 0; } }
            }
            class Program {
                public static void Main() {
                    Collection c = new Collection();
                    int v = c[{{ArgumentValue}}];
                }
            }
            """;
        }

        public override string ToString()
        {
            return $"this[int index] where index {Operator} {Threshold}, arg={ArgumentValue} → {(ShouldAccept ? "accept" : "reject")}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] Operators = [">=", "<=", ">", "<"];

    private static Gen<DependentArgScenario> GenDependentArgScenario()
    {
        return from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               from argValue in Gen.Choose(0, 100)
               select new DependentArgScenario(op, threshold, argValue);
    }

    private static Gen<MethodParamScenario> GenMethodParamScenario()
    {
        return from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               from argValue in Gen.Choose(0, 100)
               select new MethodParamScenario(op, threshold, argValue);
    }

    private static Gen<ConstructorParamScenario> GenConstructorParamScenario()
    {
        return from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               from argValue in Gen.Choose(0, 100)
               select new ConstructorParamScenario(op, threshold, argValue);
    }

    private static Gen<IndexerParamScenario> GenIndexerParamScenario()
    {
        return from op in Gen.Elements(Operators)
               from threshold in Gen.Choose(0, 100)
               from argValue in Gen.Choose(0, 100)
               select new IndexerParamScenario(op, threshold, argValue);
    }

    public static Arbitrary<DependentArgScenario> ArbitraryDependentArgScenario()
    {
        return GenDependentArgScenario().ToArbitrary();
    }

    public static Arbitrary<MethodParamScenario> ArbitraryMethodParamScenario()
    {
        return GenMethodParamScenario().ToArbitrary();
    }

    public static Arbitrary<ConstructorParamScenario> ArbitraryConstructorParamScenario()
    {
        return GenConstructorParamScenario().ToArbitrary();
    }

    public static Arbitrary<IndexerParamScenario> ArbitraryIndexerParamScenario()
    {
        return GenIndexerParamScenario().ToArbitrary();
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4a / 5a: Dependent type argument enforcement.
    ///
    /// For any dependent type parameter with an adhoc refinement constraint and any
    /// compile-time constant argument:
    /// - If the argument satisfies the predicate → no MIST_REFINEMENT_0004 diagnostic.
    /// - If the argument violates the predicate → exactly one MIST_REFINEMENT_0004 diagnostic.
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AdhocRefinementEnforcementPropertyTests) })]
    public Property DependentArg_AcceptedIffSatisfiesPredicate(DependentArgScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSource());

        var violations = diagnostics
            .Where(d => d.Id == TypeCheckerAnalyzer.RefinementConstraintViolation)
            .ToList();

        var passed = scenario.ShouldAccept
            ? violations.Count == 0
            : violations.Count == 1;

        return passed.ToProperty()
            .Label($"{scenario}. Violations found: {violations.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 4b / 5b: Method parameter refinement enforcement.
    ///
    /// For any method parameter with an adhoc refinement constraint and any
    /// compile-time constant argument:
    /// - If the argument satisfies the predicate → no MIST_REFINEMENT_0005 diagnostic.
    /// - If the argument violates the predicate → exactly one MIST_REFINEMENT_0005 diagnostic.
    ///
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AdhocRefinementEnforcementPropertyTests) })]
    public Property MethodParam_AcceptedIffSatisfiesPredicate(MethodParamScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSource());

        var violations = diagnostics
            .Where(d => d.Id == TypeCheckerAnalyzer.ParameterRefinementViolation)
            .ToList();

        var passed = scenario.ShouldAccept
            ? violations.Count == 0
            : violations.Count == 1;

        return passed.ToProperty()
            .Label($"{scenario}. Violations found: {violations.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 4c / 5c: Constructor parameter refinement enforcement.
    ///
    /// For any constructor parameter with an adhoc refinement constraint and any
    /// compile-time constant argument:
    /// - If the argument satisfies the predicate → no MIST_REFINEMENT_0005 diagnostic.
    /// - If the argument violates the predicate → exactly one MIST_REFINEMENT_0005 diagnostic.
    ///
    /// **Validates: Requirements 6.5, 6.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AdhocRefinementEnforcementPropertyTests) })]
    public Property ConstructorParam_AcceptedIffSatisfiesPredicate(ConstructorParamScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSource());

        var violations = diagnostics
            .Where(d => d.Id == TypeCheckerAnalyzer.ParameterRefinementViolation)
            .ToList();

        var passed = scenario.ShouldAccept
            ? violations.Count == 0
            : violations.Count == 1;

        return passed.ToProperty()
            .Label($"{scenario}. Violations found: {violations.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 4d / 5d: Indexer parameter refinement enforcement.
    ///
    /// For any indexer parameter with an adhoc refinement constraint and any
    /// compile-time constant argument:
    /// - If the argument satisfies the predicate → no MIST_REFINEMENT_0005 diagnostic.
    /// - If the argument violates the predicate → exactly one MIST_REFINEMENT_0005 diagnostic.
    ///
    /// **Validates: Requirements 6.6, 6.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AdhocRefinementEnforcementPropertyTests) })]
    public Property IndexerParam_AcceptedIffSatisfiesPredicate(IndexerParamScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.ToSource());

        var violations = diagnostics
            .Where(d => d.Id == TypeCheckerAnalyzer.ParameterRefinementViolation)
            .ToList();

        var passed = scenario.ShouldAccept
            ? violations.Count == 0
            : violations.Count == 1;

        return passed.ToProperty()
            .Label($"{scenario}. Violations found: {violations.Count}. " +
                   $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}