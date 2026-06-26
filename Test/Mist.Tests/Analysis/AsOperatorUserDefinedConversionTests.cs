// Feature: as-operator, Property 7: As operator does not trigger user-defined conversions
// **Validates: Requirements 7.5**
//
// Unit tests verifying that the `as` operator does NOT invoke implicit or explicit
// cast operators. When two types are unrelated (no type hierarchy, no union membership,
// no unknown relationship) but one defines a cast operator to the other, `as` must
// still classify the conversion as Invalid and emit MIST_TC_0160.
//
// The `as` operator operates purely on type hierarchy relationships, union membership,
// and unknown narrowing. User-defined conversions require the explicit cast syntax
// `(T)expr`.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Tests that the <c>as</c> operator does NOT trigger user-defined implicit or explicit
/// cast operators. When no type hierarchy relationship exists between source and target,
/// the type checker must emit <c>MIST_TC_0160</c> regardless of whether a cast operator
/// is declared.
/// </summary>
public class AsOperatorUserDefinedConversionTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    private const string AsOperatorNoRelationshipDiagnosticId = "MIST_TC_0160";

    /// <summary>
    /// Compiles Mist source and returns all diagnostics from both the semantic model
    /// builder and the type checker.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
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

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0160: as operator between unrelated types with implicit operator
    // Requirement 7.5 / Property 7
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two unrelated classes where the source declares an implicit cast operator to
    /// the target. Using <c>as</c> between them must emit MIST_TC_0160 because the
    /// <c>as</c> operator does not use user-defined conversions.
    /// </summary>
    [Fact]
    public void AsWithImplicitOperatorOnSource_BetweenUnrelatedTypes_EmitsMIST_TC_0160()
    {
        const string source =
            "class Fahrenheit {\n" +
            "    public float Degrees;\n" +
            "    public Fahrenheit(float d) { this.Degrees = d; }\n" +
            "}\n" +
            "class Celsius {\n" +
            "    public float Degrees;\n" +
            "    public Celsius(float d) { this.Degrees = d; }\n" +
            "    public static implicit operator Fahrenheit(Celsius c) {\n" +
            "        return new Fahrenheit(0.0);\n" +
            "    }\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() {\n" +
            "        Celsius c = new Celsius(100.0);\n" +
            "        var f = c as Fahrenheit;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == AsOperatorNoRelationshipDiagnosticId);
        Assert.Contains("Celsius", diag.Message);
        Assert.Contains("Fahrenheit", diag.Message);
    }

    /// <summary>
    /// Two unrelated classes where the target declares an implicit cast operator from
    /// the source. Using <c>as</c> between them must still emit MIST_TC_0160.
    /// </summary>
    [Fact]
    public void AsWithImplicitOperatorOnTarget_BetweenUnrelatedTypes_EmitsMIST_TC_0160()
    {
        const string source =
            "class Meters {\n" +
            "    public float Value;\n" +
            "    public Meters(float v) { this.Value = v; }\n" +
            "}\n" +
            "class Feet {\n" +
            "    public float Value;\n" +
            "    public Feet(float v) { this.Value = v; }\n" +
            "    public static implicit operator Feet(Meters m) {\n" +
            "        return new Feet(0.0);\n" +
            "    }\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() {\n" +
            "        Meters m = new Meters(5.0);\n" +
            "        var f = m as Feet;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == AsOperatorNoRelationshipDiagnosticId);
        Assert.Contains("Meters", diag.Message);
        Assert.Contains("Feet", diag.Message);
    }

    /// <summary>
    /// Two unrelated classes where the source declares an explicit cast operator to
    /// the target. The <c>as</c> operator must not use explicit operators either —
    /// MIST_TC_0160 must fire.
    /// </summary>
    [Fact]
    public void AsWithExplicitOperatorOnSource_BetweenUnrelatedTypes_EmitsMIST_TC_0160()
    {
        const string source =
            "class Kilograms {\n" +
            "    public float Value;\n" +
            "    public Kilograms(float v) { this.Value = v; }\n" +
            "    public static explicit operator Pounds(Kilograms k) {\n" +
            "        return new Pounds(0.0);\n" +
            "    }\n" +
            "}\n" +
            "class Pounds {\n" +
            "    public float Value;\n" +
            "    public Pounds(float v) { this.Value = v; }\n" +
            "}\n" +
            "class Program {\n" +
            "    public static void Main() {\n" +
            "        Kilograms k = new Kilograms(70.0);\n" +
            "        var p = k as Pounds;\n" +
            "    }\n" +
            "}";

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == AsOperatorNoRelationshipDiagnosticId);
        Assert.Contains("Kilograms", diag.Message);
        Assert.Contains("Pounds", diag.Message);
    }
}