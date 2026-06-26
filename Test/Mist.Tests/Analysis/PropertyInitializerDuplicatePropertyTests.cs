// Feature: property-initializers, Property 7: Duplicate member produces diagnostic
// **Validates: Requirements 6.1, 6.2**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a scenario where a property initializer block contains a duplicate member name.
/// </summary>
public sealed class DuplicateMemberScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string DuplicateName { get; }

    public DuplicateMemberScenario(string mistSource, string description, string duplicateName)
    {
        MistSource = mistSource;
        Description = description;
        DuplicateName = duplicateName;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests verifying that a duplicate member name in a property initializer
/// block produces exactly one MIST_PI_0006 diagnostic for the duplicate occurrence.
///
/// **Validates: Requirements 6.1, 6.2**
///
/// For any property initializer block containing the same member name K more than once,
/// the type checker SHALL produce exactly one MIST_PI_0006 diagnostic for the duplicate
/// occurrence.
/// </summary>
public sealed class PropertyInitializerDuplicatePropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] PropNames =
        ["Name", "Value", "Count", "Size", "Width"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<DuplicateMemberScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<DuplicateMemberScenario> GenScenario()
    {
        return from dupIndex in Gen.Choose(0, PropNames.Length - 1)
               from firstValue in Gen.Choose(0, 499)
               from secondValue in Gen.Choose(500, 999)
               from extraPropCount in Gen.Choose(0, 3)
               let dupName = PropNames[dupIndex]
               let extraProps = PropNames.Where((_, i) => i != dupIndex).Take(extraPropCount).ToArray()
               let classDecl = BuildClassDeclaration(dupName, extraProps)
               let assignments = BuildDuplicateAssignments(dupName, firstValue, secondValue, extraProps)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType() {{ {assignments} }};\n    }}\n}}"
               select new DuplicateMemberScenario(
                   source,
                   $"duplicate '{dupName}' with {extraPropCount} extra props",
                   dupName);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string BuildClassDeclaration(string dupName, string[] extraProps)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("class TestType {");
        _ = sb.AppendLine($"    public int {dupName} {{ get; set; }}");
        foreach(var prop in extraProps)
        {
            _ = sb.AppendLine($"    public int {prop} {{ get; set; }}");
        }

        _ = sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildDuplicateAssignments(
        string dupName, int firstValue, int secondValue, string[] extraProps)
    {
        var parts = new List<string>
        {
            // First occurrence of the duplicate
            $"{dupName} = {firstValue}"
        };
        // Extra valid assignments
        var extraVal = 100;
        foreach(var prop in extraProps)
        {
            parts.Add($"{prop} = {extraVal++}");
        }
        // Second occurrence of the duplicate (this triggers MIST_PI_0006)
        parts.Add($"{dupName} = {secondValue}");
        return string.Join(", ", parts);
    }

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return lexBag.ToImmutableArray();
        }

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return parseBag.ToImmutableArray();
        }

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Duplicate member produces diagnostic
    //
    // **Validates: Requirements 6.1, 6.2**
    //
    // For any property initializer block containing the same member name K
    // more than once, the type checker SHALL produce exactly one MIST_PI_0006
    // diagnostic for the duplicate occurrence.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerDuplicatePropertyTests) })]
    public Property DuplicateMember_ProducesDiagnostic(DuplicateMemberScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        var piDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PI_0006")
            .ToList();

        if(piDiags.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 MIST_PI_0006 diagnostic, got {piDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify the diagnostic message mentions the duplicate member name
        var diag = piDiags[0];
        var mentionsMember = diag.Message.Contains(scenario.DuplicateName);

        return mentionsMember.ToProperty()
            .Label($"MIST_PI_0006 diagnostic does not mention '{scenario.DuplicateName}'.\n" +
                   $"Message: {diag.Message}");
    }
}