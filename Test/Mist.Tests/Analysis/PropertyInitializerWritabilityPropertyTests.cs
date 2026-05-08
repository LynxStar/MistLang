// Feature: property-initializers, Property 5: Read-only property produces diagnostic
// **Validates: Requirements 5.1, 5.2**

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
/// Describes a scenario where a property initializer targets a read-only property (get-only).
/// </summary>
public sealed class WritabilityScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string PropertyName { get; }

    public WritabilityScenario(string mistSource, string description, string propertyName)
    {
        MistSource = mistSource;
        Description = description;
        PropertyName = propertyName;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that assigning to a read-only property (get-only, no set
/// or init accessor) in a property initializer block produces exactly one MIST_PI_0003 diagnostic.
///
/// **Validates: Requirements 5.1, 5.2**
///
/// For any type T with a property P that has only a <c>get</c> accessor (no <c>set</c> or
/// <c>init</c>), a property initializer <c>new T() { P = expr }</c> SHALL produce exactly
/// one MIST_PI_0003 diagnostic.
/// </summary>
public sealed class PropertyInitializerWritabilityPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] ReadOnlyPropNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] PropTypes =
        ["int", "string", "bool"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<WritabilityScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static Gen<WritabilityScenario> GenScenario()
    {
        return from propIndex in Gen.Choose(0, ReadOnlyPropNames.Length - 1)
               from typeIndex in Gen.Choose(0, PropTypes.Length - 1)
               from intValue in Gen.Choose(0, 999)
               let propName = ReadOnlyPropNames[propIndex]
               let propType = PropTypes[typeIndex]
               let valueExpr = GetCompatibleValue(propType, intValue)
               let classDecl = BuildClassWithGetOnlyProperty(propName, propType)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType() {{ {propName} = {valueExpr} }};\n    }}\n}}"
               select new WritabilityScenario(
                   source,
                   $"assign to get-only {propType} property '{propName}'",
                   propName);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string GetCompatibleValue(string propType, int seed)
    {
        return propType switch
        {
            "int" => seed.ToString(),
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0"
        };
    }

    private static string BuildClassWithGetOnlyProperty(string propName, string propType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("class TestType {");
        // A get-only property (no set or init)
        sb.AppendLine($"    public {propType} {propName} {{ get; }}");
        sb.AppendLine("}");
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
        if (lexBag.HasErrors)
            return lexBag.ToImmutableArray();

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if (parseBag.HasErrors || parseResult.Root is null)
            return parseBag.ToImmutableArray();

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
    // Property 5: Read-only property produces diagnostic
    //
    // **Validates: Requirements 5.1, 5.2**
    //
    // For any type T with a property P that has only a `get` accessor
    // (no `set` or `init`), a property initializer `new T() { P = expr }`
    // SHALL produce exactly one MIST_PI_0003 diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerWritabilityPropertyTests) })]
    public Property ReadOnlyProperty_ProducesDiagnostic(WritabilityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        var piDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PI_0003")
            .ToList();

        if (piDiags.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 MIST_PI_0003 diagnostic, got {piDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify the diagnostic message mentions the property name
        var diag = piDiags[0];
        var mentionsMember = diag.Message.Contains(scenario.PropertyName);

        return mentionsMember.ToProperty()
            .Label($"MIST_PI_0003 diagnostic does not mention '{scenario.PropertyName}'.\n" +
                   $"Message: {diag.Message}");
    }
}
