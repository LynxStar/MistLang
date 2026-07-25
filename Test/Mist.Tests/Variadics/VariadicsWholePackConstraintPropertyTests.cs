// Feature: variadics, task 16.4: property test for whole-pack constraint enforcement
// (classic pipeline).
//
// Property 16: Whole-pack constraint enforcement.
// **Validates: Requirements 11.1, 11.2, 11.3**
//
// A whole-pack constraint `where TArgs : C` is enforced elementwise: a non-empty binding is
// well-typed IFF every element independently satisfies C (constrained members usable on each
// element and on `args[i]`), otherwise MIST_VAR_0009 is emitted per failing element.
//
// Coverage is split by cost. A handful of representative bindings ([Theory]) compile end-to-
// end through the classic pipeline (lex/parse -> stdlib semantic model -> TypeCheckerAnalyzer)
// to prove the pipeline wires the elementwise check in and reports MIST_VAR_0009 once per
// violating element. The wide, position-independent coverage -- every struct/class pattern of
// length 1..6 -- is a property over the internal static CheckPackConstraintSatisfaction rule,
// driven directly on hand-built TypeParameterTypes with no compile, so high iteration counts
// cost almost nothing. The struct constraint is chosen because it can be oracle-decided purely
// from the declaration keyword (struct vs class) without resolving abstract-type
// implementations, making the independent oracle trivially correct.
//
// All tests run entirely in-process with ZERO external process spawns.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Whether a generated element type is declared as a struct (satisfies the struct constraint)
/// or a class (violates the struct constraint).
/// </summary>
public enum ConstraintElementKind
{
    Struct,
    Class,
}

/// <summary>
/// A generated whole-pack constraint scenario: a non-empty binding of element types where each
/// element is either a struct (satisfies) or a class (violates) the struct constraint.
/// </summary>
public sealed record WholePackConstraintScenario(IReadOnlyList<ConstraintElementKind> Elements)
{
    public int ExpectedViolations => Elements.Count(e => e == ConstraintElementKind.Class);

    public override string ToString()
    {
        var kinds = string.Join(", ", Elements.Select(e => e == ConstraintElementKind.Struct ? "struct" : "class"));
        return $"[{kinds}] violations={ExpectedViolations}";
    }
}

/// <summary>
/// Property test for <b>Property 16 (Whole-pack constraint enforcement)</b> over the classic
/// pipeline. Generates non-empty bindings with a struct constraint and asserts:
/// <list type="bullet">
///   <item>Well-typed (no MIST_VAR_0009) iff every element satisfies the constraint.</item>
///   <item>MIST_VAR_0009 count equals the number of violating elements.</item>
/// </list>
/// <b>Validates: Requirements 11.1, 11.2, 11.3.</b>
/// </summary>
public sealed class VariadicsWholePackConstraintPropertyTests
{
    private const string DiagnosticId = "MIST_VAR_0009";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 16: well-typed IFF every element satisfies C, else MIST_VAR_0009 per failing
    // element. **Validates: Requirements 11.1, 11.2, 11.3**
    // ═══════════════════════════════════════════════════════════════════════

    // End-to-end integration: a handful of representative bindings proving the classic
    // pipeline wires the elementwise constraint check in and emits MIST_VAR_0009 once per
    // violating (class) element. The wide, position-independent coverage lives in the pure-
    // rule property below, which exercises the same 1..6-element struct/class patterns
    // without paying for a full compile. Patterns: 'S' = struct (satisfies), 'C' = class
    // (violates).
    [Theory]
    [InlineData("S")]       // single struct: accepted
    [InlineData("C")]       // single class: one violation
    [InlineData("SSS")]     // all struct: accepted
    [InlineData("CCC")]     // all class: three violations
    [InlineData("SCS")]     // violation in the middle
    [InlineData("CSSSSC")]  // violations at both ends of a length-6 binding
    public void WholePackConstraint_EmitsDiagnosticPerViolatingElement(string pattern)
    {
        var elements = pattern
            .Select(c => c == 'S' ? ConstraintElementKind.Struct : ConstraintElementKind.Class)
            .ToList();
        var scenario = new WholePackConstraintScenario(elements);

        // Build Mist source: each element is a uniquely-named struct or class.
        var source = BuildSource(scenario);
        var diagnostics = AnalyzeClassic(source);

        var actualCount = diagnostics.Count(d => d.Id.Value == DiagnosticId);
        var expectedCount = scenario.ExpectedViolations;

        // IFF: well-typed (zero diagnostics) exactly when no violations exist.
        Assert.Equal(expectedCount, actualCount);

        // The IFF property itself.
        var wellTyped = actualCount == 0;
        var allSatisfy = expectedCount == 0;
        Assert.Equal(allSatisfy, wellTyped);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 16 (pure rule): CheckPackConstraintSatisfaction with a struct constraint
    // matches the oracle. **Validates: Requirements 11.1, 11.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsWholePackConstraintPropertyTests) }, MaxTest = 150)]
    public bool WholePackConstraint_PureRule_MatchesOracle(WholePackConstraintScenario scenario)
    {
        // Build a TypeParameterType with a struct constraint.
        var tp = new TypeParameterType("TArgs", 0)
        {
            IsPack = true,
            IsUnbracketedPack = true,
            Constraints = new TypeParameterConstraints { HasStructConstraint = true },
        };

        // Build element types from the palette: structs are StructTypes, classes are ClassTypes.
        var elements = scenario.Elements.Select(BuildElementType).ToArray();

        // Parse a minimal source to get a non-null SyntaxNode for diagnostic reporting.
        var dummyNode = GetDummyNode();

        var bag = new DiagnosticBag();
        TypeCheckerAnalyzer.CheckPackConstraintSatisfaction(
            elements, tp, dummyNode, model: null!, bag);

        var actualCount = bag.ToImmutableArray().Count(d => d.Id.Value == DiagnosticId);
        var expectedCount = scenario.ExpectedViolations;

        Assert.Equal(expectedCount, actualCount);
        return true;
    }

    /// <summary>
    /// Returns a minimal parsed SyntaxNode for diagnostic location reporting in direct rule calls.
    /// </summary>
    private static Storm.Runtime.SyntaxNode GetDummyNode()
    {
        if(cachedDummyNode is not null)
        {
            return cachedDummyNode;
        }

        var lexer = new MistLexer();
        var parser = new MistParser();
        var bag = new DiagnosticBag();
        var sourceText = new SourceText("dummy.mist", "class X { }");
        var tokens = lexer.Lex(sourceText, bag);
        var result = parser.Parse(tokens, bag);
        cachedDummyNode = result.Root!;
        return cachedDummyNode;
    }

    private static Storm.Runtime.SyntaxNode? cachedDummyNode;

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the properties above.</summary>
    public static Arbitrary<WholePackConstraintScenario> WholePackConstraintScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<WholePackConstraintScenario> GenScenario()
    {
        return
            from count in Gen.Choose(1, 6)
            from kinds in Gen.ArrayOf(
                Gen.Elements(ConstraintElementKind.Struct, ConstraintElementKind.Class), count)
            select new WholePackConstraintScenario(kinds);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a complete Mist source with a constrained pack <c>where TArgs : struct</c>
    /// and a construction site binding the generated element types.
    /// </summary>
    private static string BuildSource(WholePackConstraintScenario scenario)
    {
        var declarations = new System.Text.StringBuilder();
        var typeArgs = new List<string>();

        for(var i = 0; i < scenario.Elements.Count; i++)
        {
            var name = $"Elem{i}";
            typeArgs.Add(name);
            var keyword = scenario.Elements[i] == ConstraintElementKind.Struct ? "struct" : "class";
            _ = declarations.AppendLine($"{keyword} {name} {{ }}");
        }

        var argList = string.Join(", ", typeArgs);

        return $$"""
            {{declarations}}
            public class Bundle<params TArgs> where TArgs : struct
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<{{argList}}>();
                }
            }
            """;
    }

    /// <summary>
    /// Builds a lightweight IType for direct CheckPackConstraintSatisfaction calls.
    /// Structs are StructType (Kind == Struct), classes are ClassType (Kind == Class).
    /// </summary>
    private static IType BuildElementType(ConstraintElementKind kind, int index)
    {
        if(kind == ConstraintElementKind.Struct)
        {
            return new StructType($"Elem{index}", new Scope(null), containingNamespace: null);
        }

        return new ClassType($"Elem{index}", new Scope(null), containingNamespace: null);
    }

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the
    /// stdlib, runs TypeCheckerAnalyzer against it, and returns the analyzer diagnostics.
    /// No backend runs and no external process is spawned.
    /// </summary>
    private static ImmutableArray<Diagnostic> AnalyzeClassic(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(parseResult.Root!, source: source);
        var analyzerDiagnostics = new DiagnosticBag();
        new TypeCheckerAnalyzer().Analyze(model, analyzerDiagnostics);

        return analyzerDiagnostics.ToImmutableArray();
    }
}