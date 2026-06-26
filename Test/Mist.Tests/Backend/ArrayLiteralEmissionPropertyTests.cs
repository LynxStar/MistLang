// Feature: array-literals, Property 8: C# emission format
// **Validates: Requirements 7.1, 7.3, 8.1**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for C# emission format of array literals.
///
/// Property 8: C# emission format
/// For any non-empty array literal with elements [e1, ..., eN], the C# emitter SHALL
/// produce output matching the pattern <c>new[] { e1, ..., eN }</c> where each ei is
/// the C# emission of the corresponding element.
/// For any empty array literal with element type T, the emitter SHALL produce
/// <c>Array.Empty&lt;T&gt;()</c>.
///
/// **Validates: Requirements 7.1, 7.3, 8.1**
/// </summary>
public class ArrayLiteralEmissionPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static string? CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return null;
        }

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return null;
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
        if(semBag.HasErrors)
        {
            return null;
        }

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(tcErrors.Count > 0)
        {
            return null;
        }

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        if(emitBag.HasErrors)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty int array scenario: a list of 1–10 non-negative integers.
    /// </summary>
    public static Arbitrary<int[]> ArbitraryNonEmptyIntArray()
    {
        var gen = from count in Gen.Choose(1, 10)
                  from values in Gen.ArrayOf(Gen.Choose(0, 9999), count)
                  select values;
        return gen.ToArbitrary();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 8a: Non-empty array literal emits new[] { e1, e2, ... } pattern
    //
    // For any non-empty array literal with elements [e1, ..., eN], the C# emitter
    // SHALL produce output matching the pattern new[] { e1, ..., eN }.
    //
    // **Validates: Requirements 7.1, 7.3**
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralEmissionPropertyTests) })]
    public Property NonEmptyArrayLiteral_EmitsNewArrayPattern(int[] elements)
    {
        var elementList = string.Join(", ", elements);
        var source =
            "class Program {\n" +
            "    static void Main() {\n" +
            $"        var x = [{elementList}];\n" +
            "    }\n" +
            "}\n";

        var emittedCSharp = CompileAndGetEmittedCSharp(source);

        var expectedPattern = $"new[] {{ {elementList} }}";
        var containsPattern = emittedCSharp is not null && emittedCSharp.Contains(expectedPattern);

        return containsPattern
            .ToProperty()
            .Label($"Expected emitted C# to contain '{expectedPattern}' for array [{elementList}]. " +
                   $"Emitted: {emittedCSharp?[..Math.Min(emittedCSharp.Length, 500)] ?? "<null>"}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 8b: Empty array literal emits Array.Empty<T>() pattern
    //
    // For any empty array literal with element type T, the emitter SHALL produce
    // Array.Empty<T>().
    //
    // **Validates: Requirement 8.1**
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyArrayLiteral_EmitsArrayEmptyPattern()
    {
        // Empty array literals use the [] syntax with an explicit type annotation.
        // We test with int[] x = [] which should emit Array.Empty<int>().
        var source = """
            class Program {
                static void Main() {
                    int[] x = [];
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(source);

        Assert.NotNull(emittedCSharp);
        Assert.Contains("Array.Empty<int>()", emittedCSharp);
    }
}