// Feature: property-initializers, Property 8: Valid initializer produces no diagnostics
// **Validates: Requirements 3.1, 4.1, 5.1, 6.1, 7.3**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a scenario where a property initializer block is fully valid
/// (distinct, type-compatible, writable members).
/// </summary>
public sealed class ValidInitializerScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public int AssignmentCount { get; }

    public ValidInitializerScenario(string mistSource, string description, int assignmentCount)
    {
        MistSource = mistSource;
        Description = description;
        AssignmentCount = assignmentCount;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests verifying that valid property initializer blocks produce zero
/// property-initializer diagnostics (MIST_PI_*).
///
/// **Validates: Requirements 3.1, 4.1, 5.1, 6.1, 7.3**
///
/// For any type T with N settable properties/fields, and a property initializer block
/// assigning distinct, type-compatible values to a subset of those members, the type
/// checker SHALL produce zero property-initializer diagnostics.
/// </summary>
public sealed class PropertyInitializerValidPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly (string Name, string Type, string ValueExpr)[] Members =
    [
        ("Name", "string", "\"hello\""),
        ("Value", "int", "42"),
        ("Count", "int", "10"),
        ("Enabled", "bool", "true"),
        ("Label", "string", "\"test\""),
    ];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<ValidInitializerScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<ValidInitializerScenario> GenScenario()
    {
        return Gen.OneOf(
            GenWithArgs(),
            GenNoArgs());
    }

    private static Gen<ValidInitializerScenario> GenWithArgs()
    {
        return from assignCount in Gen.Choose(1, 5)
               let selectedMembers = Members.Take(assignCount).ToArray()
               let classDecl = BuildClassDeclaration("TestType", selectedMembers)
               let assignments = string.Join(", ",
                   selectedMembers.Select(m => $"{m.Name} = {m.ValueExpr}"))
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType() {{ {assignments} }};\n    }}\n}}"
               select new ValidInitializerScenario(
                   source,
                   $"valid with-args({assignCount})",
                   assignCount);
    }

    private static Gen<ValidInitializerScenario> GenNoArgs()
    {
        return from assignCount in Gen.Choose(1, 5)
               let selectedMembers = Members.Take(assignCount).ToArray()
               let classDecl = BuildClassDeclaration("TestType", selectedMembers)
               let assignments = string.Join(", ",
                   selectedMembers.Select(m => $"{m.Name} = {m.ValueExpr}"))
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType {{ {assignments} }};\n    }}\n}}"
               select new ValidInitializerScenario(
                   source,
                   $"valid no-args({assignCount})",
                   assignCount);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string BuildClassDeclaration(
        string typeName, (string Name, string Type, string ValueExpr)[] members)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"class {typeName} {{");
        foreach(var (name, type, _) in members)
        {
            _ = sb.AppendLine($"    public {type} {name} {{ get; set; }}");
        }

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
    // Property 8: Valid initializer produces no diagnostics
    //
    // **Validates: Requirements 3.1, 4.1, 5.1, 6.1, 7.3**
    //
    // For any type T with N settable properties/fields, and a property
    // initializer block assigning distinct, type-compatible values to a
    // subset of those members, the type checker SHALL produce zero
    // property-initializer diagnostics.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerValidPropertyTests) })]
    public Property ValidInitializer_ProducesNoDiagnostics(ValidInitializerScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        var piDiags = diagnostics
            .Where(d => d.Id.Value.StartsWith("MIST_PI_"))
            .ToList();

        if(piDiags.Count != 0)
        {
            return false.ToProperty()
                .Label($"Expected zero MIST_PI_* diagnostics, got {piDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"PI diagnostics: {string.Join("; ", piDiags.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }
}