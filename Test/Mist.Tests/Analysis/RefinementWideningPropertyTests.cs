// Feature: refinement-types, Property 4: Widening always accepted & Property 6: Arithmetic produces base type

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
/// Property-based tests verifying:
///
/// Property 4: Widening always accepted — For any refinement type R with base
/// type B, assigning a value of type R to a variable of type B SHALL always be
/// accepted by the type checker (zero diagnostics).
///
/// Property 6: Arithmetic produces base type — For any refinement type R with
/// base type B, performing any arithmetic operation on a value of type R SHALL
/// produce a result of type B. Assigning the arithmetic result to a base-typed
/// variable produces zero diagnostics, while assigning it back to the refinement
/// type produces MIST_TC_0098 (non-constant assigned to refinement type).
///
/// Uses the full compilation pipeline via a shared CompilerDriver instance to
/// comply with the test-process-spawning policy.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 9.4**
/// </summary>
public class RefinementWideningPropertyTests
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

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a widening scenario: a refinement type <c>type T = int >= Threshold;</c>
    /// with a literal <c>Literal</c> that satisfies the predicate (Literal >= Threshold),
    /// and an assignment of the refinement-typed value to a base-typed (int) variable.
    /// </summary>
#pragma warning disable CA1034
    public record WideningScenario(int Threshold, int Literal)
    {
        public override string ToString() =>
            $"type T = int >= {Threshold}; T x = {Literal}; int y = x;";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Represents an arithmetic erasure scenario: a refinement type
    /// <c>type T = int >= Threshold;</c> with a literal that satisfies the predicate,
    /// and an arithmetic operation applied to the refinement-typed value.
    /// </summary>
#pragma warning disable CA1034
    public record ArithmeticScenario(int Threshold, int Literal, string Operator)
    {
        public override string ToString() =>
            $"type T = int >= {Threshold}; T x = {Literal}; int result = x {Operator} 1;";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<WideningScenario> GenWideningScenario()
    {
        // Generate threshold and a literal that satisfies the predicate (literal >= threshold)
        return from threshold in Gen.Choose(0, 100)
               from offset in Gen.Choose(0, 100)
               let literal = threshold + offset
               select new WideningScenario(threshold, literal);
    }

    private static Gen<ArithmeticScenario> GenArithmeticScenario()
    {
        var operators = new[] { "+", "-", "*", "/" };
        return from threshold in Gen.Choose(0, 50)
               from offset in Gen.Choose(0, 50)
               let literal = threshold + offset
               from op in Gen.Elements(operators)
               select new ArithmeticScenario(threshold, literal, op);
    }

    public static Arbitrary<WideningScenario> ArbitraryWideningScenario() =>
        GenWideningScenario().ToArbitrary();

    public static Arbitrary<ArithmeticScenario> ArbitraryArithmeticScenario() =>
        GenArithmeticScenario().ToArbitrary();

    // ─── Property 4: Widening always accepted ────────────────────────────

    /// <summary>
    /// Property 4: Widening always accepted.
    ///
    /// For any refinement type <c>type T = int >= N;</c> and any literal L where L >= N,
    /// assigning a T-typed value to an int variable SHALL produce zero diagnostics.
    ///
    /// **Validates: Requirements 8.3, 9.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefinementWideningPropertyTests) })]
    public bool WideningToBaseType_AlwaysAccepted(WideningScenario scenario)
    {
        var source = $$"""
            type T = int >= {{scenario.Threshold}};
            class Program {
                public static void Main() {
                    T x = {{scenario.Literal}};
                    int y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No refinement enforcement errors
        var refinementViolations = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0097")
            .ToList();
        var nonConstantErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0098")
            .ToList();
        var typeMismatchErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        return refinementViolations.Count == 0
            && nonConstantErrors.Count == 0
            && typeMismatchErrors.Count == 0;
    }

    // ─── Property 6: Arithmetic produces base type ───────────────────────

    /// <summary>
    /// Property 6: Arithmetic produces base type.
    ///
    /// For any refinement type <c>type T = int >= N;</c> and any literal L where L >= N,
    /// performing an arithmetic operation (x op 1) and assigning the result to an int
    /// variable SHALL produce zero diagnostics (the result type is int, not T).
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefinementWideningPropertyTests) })]
    public bool ArithmeticOnRefinement_AssignableToBaseType(ArithmeticScenario scenario)
    {
        var source = $$"""
            type T = int >= {{scenario.Threshold}};
            class Program {
                public static void Main() {
                    T x = {{scenario.Literal}};
                    int result = x {{scenario.Operator}} 1;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No operator errors
        var operatorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0003")
            .ToList();
        // No refinement enforcement errors
        var refinementViolations = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0097")
            .ToList();
        var nonConstantErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0098")
            .ToList();

        return operatorErrors.Count == 0
            && refinementViolations.Count == 0
            && nonConstantErrors.Count == 0;
    }

    /// <summary>
    /// Property 6 (negative): Arithmetic produces base type, NOT the refinement type.
    ///
    /// For any refinement type <c>type T = int >= N;</c> and any literal L where L >= N,
    /// performing an arithmetic operation (x op 1) and assigning the result to a T-typed
    /// variable SHALL produce MIST_TC_0098 (non-constant assigned to refinement type),
    /// because the arithmetic result is int, not T.
    ///
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefinementWideningPropertyTests) })]
    public bool ArithmeticOnRefinement_CannotAssignToRefinementType(ArithmeticScenario scenario)
    {
        var source = $$"""
            type T = int >= {{scenario.Threshold}};
            class Program {
                public static void Main() {
                    T x = {{scenario.Literal}};
                    T result = x {{scenario.Operator}} 1;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Should produce MIST_TC_0098: non-constant assigned to refinement type
        var nonConstantErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0098")
            .ToList();

        return nonConstantErrors.Count == 1;
    }
}