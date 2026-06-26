// Feature: array-literals, Property 5: Homogeneous element type inference
// **Validates: Requirements 5.1, 5.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Represents a test scenario for homogeneous array literal type inference.
/// All elements have the same type T, and the test verifies compilation
/// succeeds without type errors (no MIST_TC_0095 diagnostic).
/// </summary>
public sealed class HomogeneousArrayScenario
{
    private readonly string[] _elementLiterals;

    public string TypeName { get; }
    public IReadOnlyList<string> ElementLiterals => _elementLiterals;

    public HomogeneousArrayScenario(string typeName, string[] elementLiterals)
    {
        TypeName = typeName;
        _elementLiterals = elementLiterals;
    }

    public override string ToString() =>
        $"{TypeName}[{_elementLiterals.Length}]: [{string.Join(", ", _elementLiterals)}]";
}

/// <summary>
/// Property-based tests for homogeneous element type inference.
///
/// Property 5: For any array literal where all N elements have the same type T,
/// the type checker SHALL infer the expression's type as T[] (an ArrayType with
/// ElementType = T). This is verified by confirming compilation succeeds without
/// the MIST_TC_0095 (incompatible types) diagnostic.
/// </summary>
public class ArrayLiteralTypeInferencePropertyTests
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

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<HomogeneousArrayScenario> ArbitraryScenario()
    {
        // Use non-negative integers only — negative literals are parsed as
        // unary minus expressions which require stdlib operator resolution.
        var intArrayGen =
            from count in Gen.Choose(1, 10)
            from values in Gen.ArrayOf(Gen.Choose(0, 10000), count)
            select new HomogeneousArrayScenario("int", values.Select(v => v.ToString()).ToArray());

        var stringArrayGen =
            from count in Gen.Choose(1, 10)
            from values in Gen.ArrayOf(
                Gen.Elements("hello", "world", "foo", "bar", "test", "mist", "array", "literal", "type", "check"),
                count)
            select new HomogeneousArrayScenario("string", values.Select(v => $"\"{v}\"").ToArray());

        var gen = Gen.OneOf(intArrayGen, stringArrayGen);
        return gen.ToArbitrary();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 5: Homogeneous element type inference
    //
    // For any array literal where all N elements have the same type T,
    // compilation succeeds without type errors (no MIST_TC_0095 diagnostic),
    // confirming the type checker infers the expression's type as T[].
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralTypeInferencePropertyTests) })]
    public Property HomogeneousArray_InfersCorrectType_NoIncompatibleTypeDiagnostic(
        HomogeneousArrayScenario scenario)
    {
        var elementList = string.Join(", ", scenario.ElementLiterals);
        var source = $$"""
            class Program {
                public static void Main() {
                    var x = [{{elementList}}];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var hasIncompatibleTypeError = diagnostics.Any(d => d.Id.Value == "MIST_TC_0095");
        var hasAnyError = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        return (!hasIncompatibleTypeError && !hasAnyError)
            .ToProperty()
            .Label($"Expected no type errors for homogeneous {scenario.TypeName} array [{elementList}] " +
                   $"but got: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }
}