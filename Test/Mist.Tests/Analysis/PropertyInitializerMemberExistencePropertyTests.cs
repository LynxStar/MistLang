// Feature: property-initializers, Property 3: Non-existent member produces diagnostic
// **Validates: Requirements 3.1, 3.2**

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
/// Describes a scenario where a property initializer references a non-existent member.
/// </summary>
public sealed class MemberExistenceScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string InvalidMemberName { get; }

    public MemberExistenceScenario(string mistSource, string description, string invalidMemberName)
    {
        MistSource = mistSource;
        Description = description;
        InvalidMemberName = invalidMemberName;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests verifying that referencing a non-existent member in a property
/// initializer block produces exactly one MIST_PI_0001 diagnostic.
///
/// **Validates: Requirements 3.1, 3.2**
///
/// For any type T and any identifier name that is not a declared property or field on T,
/// a property initializer <c>new T() { name = expr }</c> SHALL produce exactly one
/// MIST_PI_0001 diagnostic.
/// </summary>
public sealed class PropertyInitializerMemberExistencePropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] ValidPropNames =
        ["Name", "Value", "Count", "Size", "Width"];

    private static readonly string[] InvalidPropNames =
        ["Foo", "Bar", "Baz", "Qux", "Quux", "Corge", "Grault", "Garply", "Waldo", "Fred"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<MemberExistenceScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<MemberExistenceScenario> GenScenario()
    {
        return from validPropCount in Gen.Choose(1, 5)
               from invalidName in Gen.Elements(InvalidPropNames)
               from intValue in Gen.Choose(0, 999)
               let validProps = ValidPropNames.Take(validPropCount).ToArray()
               let classDecl = BuildClassDeclaration("TestType", validProps)
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType() {{ {invalidName} = {intValue} }};\n    }}\n}}"
               select new MemberExistenceScenario(
                   source,
                   $"invalid-member '{invalidName}' on TestType with {validPropCount} valid props",
                   invalidName);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string BuildClassDeclaration(string typeName, string[] propNames)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"class {typeName} {{");
        foreach(var prop in propNames)
        {
            _ = sb.AppendLine($"    public int {prop} {{ get; set; }}");
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

        // Combine semantic + type checker diagnostics
        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Non-existent member produces diagnostic
    //
    // **Validates: Requirements 3.1, 3.2**
    //
    // For any type T and any identifier name that is not a declared property
    // or field on T, a property initializer `new T() { name = expr }` SHALL
    // produce exactly one MIST_PI_0001 diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerMemberExistencePropertyTests) })]
    public Property NonExistentMember_ProducesDiagnostic(MemberExistenceScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        var piDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PI_0001")
            .ToList();

        if(piDiags.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 MIST_PI_0001 diagnostic, got {piDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify the diagnostic message mentions the invalid member name
        var diag = piDiags[0];
        var mentionsMember = diag.Message.Contains(scenario.InvalidMemberName);

        return mentionsMember.ToProperty()
            .Label($"MIST_PI_0001 diagnostic does not mention '{scenario.InvalidMemberName}'.\n" +
                   $"Message: {diag.Message}");
    }
}