// Feature: property-initializers, Property 6: Readonly field produces diagnostic
// **Validates: Requirements 5.3, 5.4**

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
/// Describes a scenario where a property initializer targets a readonly field.
/// </summary>
public sealed class ReadonlyFieldScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string FieldName { get; }

    public ReadonlyFieldScenario(string mistSource, string description, string fieldName)
    {
        MistSource = mistSource;
        Description = description;
        FieldName = fieldName;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that assigning to a readonly field in a property
/// initializer block produces exactly one MIST_PI_0004 diagnostic.
///
/// **Validates: Requirements 5.3, 5.4**
///
/// For any type T with a field F marked <c>readonly</c>, a property initializer
/// <c>new T() { F = expr }</c> SHALL produce exactly one MIST_PI_0004 diagnostic.
/// </summary>
public sealed class PropertyInitializerReadonlyFieldPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon"];

    private static readonly string[] FieldTypes =
        ["int", "string", "bool"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<ReadonlyFieldScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static Gen<ReadonlyFieldScenario> GenScenario()
    {
        return from fieldIndex in Gen.Choose(0, FieldNames.Length - 1)
               from typeIndex in Gen.Choose(0, FieldTypes.Length - 1)
               from intValue in Gen.Choose(0, 999)
               let fieldName = FieldNames[fieldIndex]
               let fieldType = FieldTypes[typeIndex]
               let valueExpr = GetCompatibleValue(fieldType, intValue)
               let classDecl = BuildClassWithReadonlyField(fieldName, fieldType)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType() {{ {fieldName} = {valueExpr} }};\n    }}\n}}"
               select new ReadonlyFieldScenario(
                   source,
                   $"assign to readonly {fieldType} field '{fieldName}'",
                   fieldName);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string GetCompatibleValue(string fieldType, int seed)
    {
        return fieldType switch
        {
            "int" => seed.ToString(),
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0"
        };
    }

    private static string BuildClassWithReadonlyField(string fieldName, string fieldType)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("class TestType {");
        _ = sb.AppendLine($"    public readonly {fieldType} {fieldName};");
        _ = sb.AppendLine("}");
        return sb.ToString();
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
    // Property 6: Readonly field produces diagnostic
    //
    // **Validates: Requirements 5.3, 5.4**
    //
    // For any type T with a field F marked `readonly`, a property initializer
    // `new T() { F = expr }` SHALL produce exactly one MIST_PI_0004 diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerReadonlyFieldPropertyTests) })]
    public Property ReadonlyField_ProducesDiagnostic(ReadonlyFieldScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        var piDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PI_0004")
            .ToList();

        if(piDiags.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 MIST_PI_0004 diagnostic, got {piDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify the diagnostic message mentions the field name
        var diag = piDiags[0];
        var mentionsMember = diag.Message.Contains(scenario.FieldName);

        return mentionsMember.ToProperty()
            .Label($"MIST_PI_0004 diagnostic does not mention '{scenario.FieldName}'.\n" +
                   $"Message: {diag.Message}");
    }
}