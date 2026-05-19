// Feature: structured-concurrency, Property 10: Await Requires Task Operand
// **Validates: Requirements 4.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that <c>MIST_TC_0105</c> is emitted when <c>await</c>
/// is applied to an expression whose type is not <c>Task&lt;T&gt;</c>.
///
/// **Validates: Requirements 4.3**
///
/// For any use of the <c>await</c> functor where the operand's type is not
/// <c>Task&lt;T&gt;</c>, the type checker SHALL emit diagnostic <c>MIST_TC_0105</c>
/// including the actual type of the operand.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AwaitOperandDiagnosticPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// The kind of non-Task type used as the operand of <c>await</c>.
    /// </summary>
    public enum NonTaskOperandKind
    {
        /// <summary>An <c>int</c> variable: <c>var x = 42; await x;</c></summary>
        IntVariable,

        /// <summary>A <c>string</c> variable: <c>var x = "hello"; await x;</c></summary>
        StringVariable,

        /// <summary>A <c>bool</c> variable: <c>var x = true; await x;</c></summary>
        BoolVariable,
    }

    /// <summary>
    /// A scenario describing a program where <c>await</c> is applied to a non-Task operand.
    /// </summary>
    public record AwaitNonTaskScenario(
        NonTaskOperandKind OperandKind,
        string ClassName,
        string MethodName,
        string VarName)
    {
        /// <summary>Gets the expected type name substring in the diagnostic message.</summary>
        public string ExpectedTypeName => OperandKind switch
        {
            NonTaskOperandKind.IntVariable => "int",
            NonTaskOperandKind.StringVariable => "string",
            NonTaskOperandKind.BoolVariable => "bool",
            _ => "unknown",
        };

        public override string ToString() =>
            $"await {OperandKind} in {ClassName}.{MethodName}";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Worker", "Service", "Handler", "Processor", "Runner"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Handle"];

    private static readonly string[] VarNames =
        ["x", "y", "result", "value", "temp"];

    public static Gen<AwaitNonTaskScenario> GenScenario()
    {
        return from operandKind in Gen.Elements(
                   NonTaskOperandKind.IntVariable,
                   NonTaskOperandKind.StringVariable,
                   NonTaskOperandKind.BoolVariable)
               from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from varName in Gen.Elements(VarNames)
               select new AwaitNonTaskScenario(operandKind, className, methodName, varName);
    }

    public static Arbitrary<AwaitNonTaskScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source construction ─────────────────────────────────────────────

    private static string BuildSource(AwaitNonTaskScenario scenario)
    {
        var varDecl = scenario.OperandKind switch
        {
            NonTaskOperandKind.IntVariable => $"var {scenario.VarName} = 42;",
            NonTaskOperandKind.StringVariable => $"var {scenario.VarName} = \"hello\";",
            NonTaskOperandKind.BoolVariable => $"var {scenario.VarName} = true;",
            _ => $"var {scenario.VarName} = 0;",
        };

        return $$"""
            class {{scenario.ClassName}} {
                public void {{scenario.MethodName}}() {
                    {{varDecl}}
                    var awaited = await {{scenario.VarName}};
                }
            }
            class Program { public static void Main() {} }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Await Requires Task Operand
    //
    // **Validates: Requirements 4.3**
    //
    // For any use of the `await` functor where the operand's type is not
    // `Task<T>`, the type checker SHALL emit diagnostic `MIST_TC_0105`
    // including the actual type of the operand.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AwaitOperandDiagnosticPropertyTests) })]
    public Property AwaitOnNonTaskOperand_EmitsMIST_TC_0105(AwaitNonTaskScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);

        var matchingDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0105")
            .ToList();

        var hasDiagnostic = matchingDiagnostics.Count > 0;

        return hasDiagnostic.ToProperty()
            .Label($"Expected MIST_TC_0105 for await on {scenario.OperandKind} but got none.\n" +
                   $"Source:\n{source}\n" +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AwaitOperandDiagnosticPropertyTests) })]
    public Property AwaitOnNonTaskOperand_DiagnosticIncludesActualType(AwaitNonTaskScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);

        var matchingDiagnostic = diagnostics
            .FirstOrDefault(d => d.Id.Value == "MIST_TC_0105");

        if (matchingDiagnostic is null)
            return false.ToProperty()
                .Label($"No MIST_TC_0105 diagnostic found for await on {scenario.OperandKind}.");

        // The diagnostic message should mention the actual type of the operand
        var messageIncludesType = matchingDiagnostic.Message.Contains(
            scenario.ExpectedTypeName, StringComparison.OrdinalIgnoreCase);

        return messageIncludesType.ToProperty()
            .Label($"MIST_TC_0105 message does not include actual type '{scenario.ExpectedTypeName}'.\n" +
                   $"Message: {matchingDiagnostic.Message}");
    }
}
