// Feature: mist-regex-type, Property 6: Regex refinement type validation
// **Validates: Requirements 11.2, 11.5, 11.6**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for regex refinement type validation.
///
/// Property 6: Regex refinement type validation.
/// For any regex-refined string type T = string `P` where P is a valid regex pattern,
/// and for any string literal L:
/// - If L fully matches P, assignment of L to T SHALL be accepted at compile time.
/// - If L does NOT fully match P, assignment of L to T SHALL produce MIST_TC_0097.
/// - Assignment of a non-constant string to T SHALL produce MIST_TC_0098.
///
/// **Validates: Requirements 11.2, 11.5, 11.6**
/// </summary>
public class RegexRefinementTypePropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Compilation Helper ─────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        ReadonlyInferenceEngine.RunInference(model);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// A set of simple, predictable regex patterns paired with generators for
    /// matching and non-matching strings.
    /// </summary>
    private static readonly (string Pattern, Func<Gen<string>> MatchGen, Func<Gen<string>> NonMatchGen)[] PatternSpecs =
    [
        (
            "^[a-z]+$",
            () => from len in Gen.Choose(1, 10)
                  from chars in Gen.ArrayOf(Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'), len)
                  select new string(chars),
            () => Gen.Elements("ABC", "123", "abc123", "Hello", "a b", "")
        ),
        (
            "^[0-9]+$",
            () => from len in Gen.Choose(1, 10)
                  from chars in Gen.ArrayOf(Gen.Elements('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), len)
                  select new string(chars),
            () => Gen.Elements("abc", "12.34", "1a2", " 123", "")
        ),
        (
            "^.{3}$",
            () => from chars in Gen.ArrayOf(Gen.Elements('a', 'b', 'c', 'x', 'y', 'z', '1', '2', '3'), 3)
                  select new string(chars),
            () => Gen.Elements("ab", "abcd", "a", "abcde", "")
        ),
        (
            "^[A-Z][a-z]+$",
            () => from first in Gen.Elements('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H')
                  from restLen in Gen.Choose(1, 8)
                  from rest in Gen.ArrayOf(Gen.Elements('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'), restLen)
                  select first + new string(rest),
            () => Gen.Elements("abc", "ABC", "aBC", "A", "123", "")
        ),
        (
            "^\\d{2,4}$",
            () => from len in Gen.Choose(2, 4)
                  from chars in Gen.ArrayOf(Gen.Elements('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'), len)
                  select new string(chars),
            () => Gen.Elements("1", "12345", "abc", "1a", "")
        ),
    ];

    /// <summary>
    /// Scenario for testing a matching string literal assigned to a regex-refined type.
    /// </summary>
#pragma warning disable CA1034
    public record RefinementMatchScenario(string Pattern, string Literal)
    {
        public override string ToString() => $"type T = string `{Pattern}`; T x = \"{Literal}\";";
    }

    /// <summary>
    /// Scenario for testing a non-matching string literal assigned to a regex-refined type.
    /// </summary>
    public record RefinementNonMatchScenario(string Pattern, string Literal)
    {
        public override string ToString() => $"type T = string `{Pattern}`; T x = \"{Literal}\";";
    }

    /// <summary>
    /// Scenario for testing a non-constant string assigned to a regex-refined type.
    /// </summary>
    public record RefinementNonConstantScenario(string Pattern)
    {
        public override string ToString() => $"type T = string `{Pattern}`; T x = someVar;";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scenarios where a matching string literal is assigned to a regex-refined type.
    /// </summary>
    private static Gen<RefinementMatchScenario> GenMatchScenario()
    {
        return from specIndex in Gen.Choose(0, PatternSpecs.Length - 1)
               let spec = PatternSpecs[specIndex]
               from literal in spec.MatchGen()
               select new RefinementMatchScenario(spec.Pattern, literal);
    }

    /// <summary>
    /// Generates scenarios where a non-matching string literal is assigned to a regex-refined type.
    /// </summary>
    private static Gen<RefinementNonMatchScenario> GenNonMatchScenario()
    {
        return from specIndex in Gen.Choose(0, PatternSpecs.Length - 1)
               let spec = PatternSpecs[specIndex]
               from literal in spec.NonMatchGen()
               select new RefinementNonMatchScenario(spec.Pattern, literal);
    }

    /// <summary>
    /// Generates scenarios where a non-constant string is assigned to a regex-refined type.
    /// </summary>
    private static Gen<RefinementNonConstantScenario> GenNonConstantScenario()
    {
        return from specIndex in Gen.Choose(0, PatternSpecs.Length - 1)
               let spec = PatternSpecs[specIndex]
               select new RefinementNonConstantScenario(spec.Pattern);
    }

    /// <summary>
    /// FsCheck Arbitrary instances for the property tests.
    /// </summary>
    public static Arbitrary<RefinementMatchScenario> ArbitraryRefinementMatchScenario()
    {
        return GenMatchScenario().ToArbitrary();
    }

    public static Arbitrary<RefinementNonMatchScenario> ArbitraryRefinementNonMatchScenario()
    {
        return GenNonMatchScenario().ToArbitrary();
    }

    public static Arbitrary<RefinementNonConstantScenario> ArbitraryRefinementNonConstantScenario()
    {
        return GenNonConstantScenario().ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Escapes a string literal for embedding in Mist source code.
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// Builds a Mist source program that declares a regex-refined type and assigns a string literal to it.
    /// </summary>
    private static string BuildRefinementAssignmentSource(string pattern, string literal)
    {
        var escaped = EscapeStringLiteral(literal);
        return $$"""
            type T = string `{{pattern}}`;
            class Program {
                public static void Main() {
                    T x = "{{escaped}}";
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source program that declares a regex-refined type and assigns a non-constant string to it.
    /// </summary>
    private static string BuildNonConstantAssignmentSource(string pattern)
    {
        return $$"""
            type T = string `{{pattern}}`;
            class Program {
                public static void Main() {
                    string input = "hello";
                    T x = input;
                }
            }
            """;
    }

    // ─── Property 6: Regex refinement type validation ───────────────────

    /// <summary>
    /// Property 6a: When a string literal fully matches the regex pattern of a refined type,
    /// assignment SHALL be accepted at compile time (no MIST_TC_0097 diagnostic).
    ///
    /// **Validates: Requirements 11.2, 11.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexRefinementTypePropertyTests) })]
    public Property RefinementType_MatchingLiteral_IsAccepted(RefinementMatchScenario scenario)
    {
        // Sanity check: verify the literal actually matches the pattern using .NET regex
        var regex = new Regex($"^(?:{scenario.Pattern.TrimStart('^').TrimEnd('$')})$");
        if(!regex.IsMatch(scenario.Literal))
        {
            // Generator produced a bad scenario — skip (discard)
            return true.ToProperty().Label("discarded: generator produced non-matching literal");
        }

        var source = BuildRefinementAssignmentSource(scenario.Pattern, scenario.Literal);
        var diagnostics = CompileAndGetDiagnostics(source);
        var refinementFail = WithId(diagnostics, "MIST_TC_0097");

        return (refinementFail.Length == 0).ToProperty()
            .Label($"Expected no MIST_TC_0097 but got {refinementFail.Length}.\n" +
                   $"Pattern: {scenario.Pattern}\n" +
                   $"Literal: \"{scenario.Literal}\"\n" +
                   $"Source:\n{source}\n" +
                   $"Diagnostics: {string.Join("; ", refinementFail.Select(d => d.Message))}");
    }

    /// <summary>
    /// Property 6b: When a string literal does NOT fully match the regex pattern of a refined type,
    /// assignment SHALL produce a MIST_TC_0097 diagnostic.
    ///
    /// **Validates: Requirements 11.2, 11.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexRefinementTypePropertyTests) })]
    public Property RefinementType_NonMatchingLiteral_IsRejected(RefinementNonMatchScenario scenario)
    {
        // Sanity check: verify the literal does NOT match the pattern
        var regex = new Regex(scenario.Pattern);
        if(regex.IsMatch(scenario.Literal))
        {
            // Generator produced a matching literal — skip (discard)
            return true.ToProperty().Label("discarded: generator produced matching literal");
        }

        var source = BuildRefinementAssignmentSource(scenario.Pattern, scenario.Literal);
        var diagnostics = CompileAndGetDiagnostics(source);
        var refinementFail = WithId(diagnostics, "MIST_TC_0097");

        return (refinementFail.Length == 1).ToProperty()
            .Label($"Expected exactly 1 MIST_TC_0097 but got {refinementFail.Length}.\n" +
                   $"Pattern: {scenario.Pattern}\n" +
                   $"Literal: \"{scenario.Literal}\"\n" +
                   $"Source:\n{source}\n" +
                   $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}");
    }

    /// <summary>
    /// Property 6c: When a non-constant string expression is assigned to a regex-refined type,
    /// the compiler SHALL produce a MIST_TC_0098 diagnostic.
    ///
    /// **Validates: Requirements 11.6**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RegexRefinementTypePropertyTests) })]
    public Property RefinementType_NonConstantString_IsRejected(RefinementNonConstantScenario scenario)
    {
        var source = BuildNonConstantAssignmentSource(scenario.Pattern);
        var diagnostics = CompileAndGetDiagnostics(source);
        var nonConstDiag = WithId(diagnostics, "MIST_TC_0098");

        return (nonConstDiag.Length == 1).ToProperty()
            .Label($"Expected exactly 1 MIST_TC_0098 but got {nonConstDiag.Length}.\n" +
                   $"Pattern: {scenario.Pattern}\n" +
                   $"Source:\n{source}\n" +
                   $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}");
    }
}