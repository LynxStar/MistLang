// Feature: refinement-types — Unit tests for C# emitter refinement type parameter guard emission
// **Validates: Requirements 12.4**

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that methods with refinement-typed parameters emit
/// runtime validation guards at method entry. The guard negates the predicate
/// condition and throws <see cref="ArgumentException"/> if the value fails.
///
/// **Validates: Requirements 12.4**
/// </summary>
public sealed class RefinementTypeParameterGuardEmissionTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    private static string CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(tcErrors);

        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    /// <summary>
    /// Verifies that a method with a single refinement-typed parameter emits
    /// a runtime guard that throws ArgumentException when the predicate fails.
    /// </summary>
    [Fact]
    public void MethodParameter_SimpleRefinement_EmitsGuard()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    Program.Greet(20);
                }
                public static void Greet(AdultAge age) {
                    int x = age;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the negated predicate check
        Assert.Contains("if (!(age >= 18))", output);
        // Should throw ArgumentException with predicate description
        Assert.Contains("throw new ArgumentException", output);
        Assert.Contains("nameof(age)", output);
        Assert.Contains(">= 18", output);
        Assert.Contains("AdultAge", output);
    }

    /// <summary>
    /// Verifies that a method with a compound refinement-typed parameter emits
    /// a guard with combined conditions.
    /// </summary>
    [Fact]
    public void MethodParameter_CompoundRefinement_EmitsGuardWithCombinedConditions()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main(string[] args) {
                    Program.SetValue(50);
                }
                public static void SetValue(Percentage pct) {
                    int x = pct;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the negated combined predicate check with && conditions
        Assert.Contains("pct >= 0", output);
        Assert.Contains("pct <= 100", output);
        Assert.Contains("&&", output);
        Assert.Contains("if (!", output);
        // Should throw ArgumentException with predicate description
        Assert.Contains("throw new ArgumentException", output);
        Assert.Contains("nameof(pct)", output);
        Assert.Contains("Percentage", output);
    }

    /// <summary>
    /// Verifies that a method with a non-refinement parameter does NOT emit a guard.
    /// </summary>
    [Fact]
    public void MethodParameter_NonRefinement_NoGuard()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Greet(25);
                }
                public static void Greet(int age) {
                    int x = age;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should NOT emit any ArgumentException guard
        Assert.DoesNotContain("throw new ArgumentException", output);
    }

    /// <summary>
    /// Verifies that the parameter type is emitted as the base type (int)
    /// while the guard is still present.
    /// </summary>
    [Fact]
    public void MethodParameter_RefinementType_EmitsBaseTypeInSignature()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main(string[] args) {
                    Program.Greet(20);
                }
                public static void Greet(AdultAge age) {
                    int x = age;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // The parameter should be emitted as the base type `int`
        Assert.Contains("int age", output);
        // Should NOT contain the refinement type name in the signature
        Assert.DoesNotContain("AdultAge age", output);
    }
}