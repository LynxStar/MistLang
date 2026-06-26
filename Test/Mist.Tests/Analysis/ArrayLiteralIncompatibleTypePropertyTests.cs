// Feature: array-literals, Property 6: Incompatible element type diagnostic
// **Validates: Requirements 5.2**

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
/// Represents a test scenario for an array literal with incompatible element types.
/// The array mixes int literals and string literals, which are incompatible in Mist.
/// </summary>
public sealed class IncompatibleArrayScenario
{
    private readonly string[] _elementLiterals;

    public IReadOnlyList<string> ElementLiterals => _elementLiterals;

    public IncompatibleArrayScenario(string[] elementLiterals)
    {
        _elementLiterals = elementLiterals;
    }

    public override string ToString() =>
        $"mixed[{_elementLiterals.Length}]: [{string.Join(", ", _elementLiterals)}]";
}

/// <summary>
/// Property-based tests for incompatible element type diagnostic.
///
/// Property 6: For any array literal containing elements of two types T1 and T2
/// where T1 is not assignable to T2 and T2 is not assignable to T1, the type checker
/// SHALL emit a diagnostic error indicating incompatible element types (MIST_TC_0095).
/// </summary>
public class ArrayLiteralIncompatibleTypePropertyTests
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
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());

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

    public static Arbitrary<IncompatibleArrayScenario> ArbitraryScenario()
    {
        // Generate arrays that mix int literals and string literals.
        // We ensure at least one int and at least one string are present.
        var intLiterals = Gen.Choose(0, 10000).Select(v => v.ToString());
        var stringLiterals = Gen.Elements(
            "\"hello\"", "\"world\"", "\"foo\"", "\"bar\"",
            "\"test\"", "\"mist\"", "\"array\"", "\"literal\"");

        var gen =
            from intCount in Gen.Choose(1, 5)
            from stringCount in Gen.Choose(1, 5)
            from ints in Gen.ArrayOf(intLiterals, intCount)
            from strings in Gen.ArrayOf(stringLiterals, stringCount)
            from shuffled in Gen.Shuffle(ints.Concat(strings).ToArray())
            select new IncompatibleArrayScenario(shuffled.ToArray());

        return gen.ToArbitrary();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 6: Incompatible element type diagnostic
    //
    // For any array literal containing elements of two types T1 and T2 where
    // T1 is not assignable to T2 and T2 is not assignable to T1, the type
    // checker SHALL emit a diagnostic error (MIST_TC_0095) indicating
    // incompatible element types.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralIncompatibleTypePropertyTests) })]
    public Property IncompatibleArray_EmitsIncompatibleTypeDiagnostic(
        IncompatibleArrayScenario scenario)
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

        return hasIncompatibleTypeError
            .ToProperty()
            .Label($"Expected MIST_TC_0095 for mixed int/string array [{elementList}] " +
                   $"but got diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }
}