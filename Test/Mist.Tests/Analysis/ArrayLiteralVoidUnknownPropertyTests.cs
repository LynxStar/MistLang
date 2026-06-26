// Feature: array-literals, Property 7: Void and unknown element rejection
// **Validates: Requirements 6.3, 6.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Represents a test scenario for an array literal containing a void-returning
/// method call as an element, alongside zero or more valid int elements.
/// </summary>
public sealed class VoidElementArrayScenario
{
    public int ValidElementsBefore { get; }
    public int ValidElementsAfter { get; }

    public VoidElementArrayScenario(int validElementsBefore, int validElementsAfter)
    {
        ValidElementsBefore = validElementsBefore;
        ValidElementsAfter = validElementsAfter;
    }

    public string BuildSource()
    {
        var elements = new List<string>();
        for(var i = 0; i < ValidElementsBefore; i++)
        {
            elements.Add((i + 1).ToString());
        }

        elements.Add("Main()"); // void-returning method call
        for(var i = 0; i < ValidElementsAfter; i++)
        {
            elements.Add((i + 100).ToString());
        }

        var elementList = string.Join(", ", elements);
        return $$"""
            class Program {
                public static void Main() {
                    var x = [{{elementList}}];
                }
            }
            """;
    }

    public override string ToString()
    {
        return $"VoidElement(before={ValidElementsBefore}, after={ValidElementsAfter})";
    }
}

/// <summary>
/// Represents a test scenario for an array literal containing an unknown-typed
/// variable as an element, alongside zero or more valid int elements.
/// </summary>
public sealed class UnknownElementArrayScenario
{
    public int ValidElementsBefore { get; }
    public int ValidElementsAfter { get; }

    public UnknownElementArrayScenario(int validElementsBefore, int validElementsAfter)
    {
        ValidElementsBefore = validElementsBefore;
        ValidElementsAfter = validElementsAfter;
    }

    public string BuildSource()
    {
        var elements = new List<string>();
        for(var i = 0; i < ValidElementsBefore; i++)
        {
            elements.Add((i + 1).ToString());
        }

        elements.Add("u"); // unknown-typed variable
        for(var i = 0; i < ValidElementsAfter; i++)
        {
            elements.Add((i + 100).ToString());
        }

        var elementList = string.Join(", ", elements);
        return $$"""
            class Program {
                public static void Main() {
                    unknown u = unknown;
                    var x = [{{elementList}}];
                }
            }
            """;
    }

    public override string ToString()
    {
        return $"UnknownElement(before={ValidElementsBefore}, after={ValidElementsAfter})";
    }
}

/// <summary>
/// Property-based tests for void and unknown element rejection in array literals.
///
/// Property 7: For any array literal containing at least one element whose type is
/// void or contains unknown, the type checker SHALL emit a diagnostic error rejecting
/// the element type.
/// </summary>
public class ArrayLiteralVoidUnknownPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

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

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ─── Generators ─────────────────────────────────────────────────────────

    public static Arbitrary<VoidElementArrayScenario> ArbitraryVoidScenario()
    {
        var gen =
            from before in Gen.Choose(0, 5)
            from after in Gen.Choose(0, 5)
            select new VoidElementArrayScenario(before, after);

        return gen.ToArbitrary();
    }

    public static Arbitrary<UnknownElementArrayScenario> ArbitraryUnknownScenario()
    {
        var gen =
            from before in Gen.Choose(0, 5)
            from after in Gen.Choose(0, 5)
            select new UnknownElementArrayScenario(before, after);

        return gen.ToArbitrary();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 7a: Void element rejection
    //
    // For any array literal containing at least one element whose type is void
    // (a call to a void-returning method), the type checker SHALL emit
    // diagnostic MIST_TC_0034.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralVoidUnknownPropertyTests) })]
    public Property VoidElement_EmitsVoidRejectionDiagnostic(
        VoidElementArrayScenario scenario)
    {
        var source = scenario.BuildSource();
        var diagnostics = CompileAndGetDiagnostics(source);

        var hasVoidRejection = diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value == "MIST_TC_0034");

        return hasVoidRejection
            .ToProperty()
            .Label($"Expected MIST_TC_0034 for {scenario} " +
                   $"but got: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 7b: Unknown element rejection
    //
    // For any array literal containing at least one element whose type contains
    // unknown, the type checker SHALL emit diagnostic MIST_SB_0055
    // (UnknownAsArrayElement).
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralVoidUnknownPropertyTests) })]
    public Property UnknownElement_EmitsUnknownRejectionDiagnostic(
        UnknownElementArrayScenario scenario)
    {
        var source = scenario.BuildSource();
        var diagnostics = CompileAndGetDiagnostics(source);

        var hasUnknownRejection = diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value == "MIST_SB_0055" ||
             d.Id.Value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
             d.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase)));

        return hasUnknownRejection
            .ToProperty()
            .Label($"Expected MIST_SB_0055 (or unknown-related error) for {scenario} " +
                   $"but got: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }
}