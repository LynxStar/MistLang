// Feature: property-initializers, Property 4: Type-incompatible assignment produces diagnostic
// **Validates: Requirements 4.1, 4.2**

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
/// Describes a scenario where a property initializer assigns a type-incompatible value.
/// </summary>
public sealed class TypeCompatibilityScenario
{
    public string MistSource { get; }
    public string Description { get; }
    public string PropertyName { get; }
    public string PropertyType { get; }
    public string ValueType { get; }

    public TypeCompatibilityScenario(
        string mistSource, string description,
        string propertyName, string propertyType, string valueType)
    {
        MistSource = mistSource;
        Description = description;
        PropertyName = propertyName;
        PropertyType = propertyType;
        ValueType = valueType;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that assigning a type-incompatible value in a property
/// initializer block produces exactly one MIST_PI_0002 diagnostic.
///
/// **Validates: Requirements 4.1, 4.2**
///
/// For any type T with a settable property P of type A, and any expression whose type B
/// is not assignable to A, a property initializer <c>new T() { P = expr }</c> SHALL produce
/// exactly one MIST_PI_0002 diagnostic.
/// </summary>
public sealed class PropertyInitializerTypeCompatibilityPropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Type mismatch pairs: (propertyType, incompatibleValueExpr, valueTypeDesc) ──

    private static readonly (string PropType, string ValueExpr, string ValueTypeDesc)[] TypeMismatches =
    [
        ("int", "\"hello\"", "string"),
        ("int", "true", "bool"),
        ("bool", "42", "int"),
        ("bool", "\"yes\"", "string"),
        ("string", "123", "int"),
        ("string", "false", "bool"),
    ];

    private static readonly string[] PropNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<TypeCompatibilityScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static Gen<TypeCompatibilityScenario> GenScenario()
    {
        return from mismatchIndex in Gen.Choose(0, TypeMismatches.Length - 1)
               from propIndex in Gen.Choose(0, PropNames.Length - 1)
               let mismatch = TypeMismatches[mismatchIndex]
               let propName = PropNames[propIndex]
               let classDecl = $"class TestType {{\n    public {mismatch.PropType} {propName} {{ get; set; }}\n}}"
               let source = $"{classDecl}\nclass Program {{\n    public static void Main() {{\n        var x = new TestType() {{ {propName} = {mismatch.ValueExpr} }};\n    }}\n}}"
               select new TypeCompatibilityScenario(
                   source,
                   $"assign {mismatch.ValueTypeDesc} to {mismatch.PropType} property '{propName}'",
                   propName,
                   mismatch.PropType,
                   mismatch.ValueTypeDesc);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
            return lexBag.ToImmutableArray();

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
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
    // Property 4: Type-incompatible assignment produces diagnostic
    //
    // **Validates: Requirements 4.1, 4.2**
    //
    // For any type T with a settable property P of type A, and any expression
    // whose type B is not assignable to A, a property initializer
    // `new T() { P = expr }` SHALL produce exactly one MIST_PI_0002 diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyInitializerTypeCompatibilityPropertyTests) })]
    public Property TypeIncompatibleAssignment_ProducesDiagnostic(TypeCompatibilityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        var piDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PI_0002")
            .ToList();

        if(piDiags.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 MIST_PI_0002 diagnostic, got {piDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"All diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify the diagnostic message mentions the property name
        var diag = piDiags[0];
        var mentionsMember = diag.Message.Contains(scenario.PropertyName);

        return mentionsMember.ToProperty()
            .Label($"MIST_PI_0002 diagnostic does not mention '{scenario.PropertyName}'.\n" +
                   $"Message: {diag.Message}");
    }
}