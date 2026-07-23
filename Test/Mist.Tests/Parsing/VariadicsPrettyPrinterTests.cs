// Feature: variadics - Unit tests for pretty-printer handling of variadic surface forms (Task 2.3)
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests asserting the EXACT text the pretty-printer emits for every variadic
/// surface form:
/// <list type="bullet">
///   <item>the <c>params TArgs</c> pack spelling, preserving the declared identifier;</item>
///   <item>the explicit <c>Type[]:TArgs</c> and fixed-size <c>Type[N]:TArgs</c> domains;</item>
///   <item>the spread operand followed by <c>...</c> - both a type-pack (<c>TArgs...</c>)
///     and a tuple value (<c>args...</c>);</item>
///   <item>value-side <c>params</c> - a value pack (<c>params TArgs args</c>) and a
///     homogeneous value-level array (<c>params string[] parts</c>);</item>
///   <item>flat (<c>Foo&lt;int, string&gt;</c>) versus bracketed
///     (<c>Foo&lt;[int, string]&gt;</c>) type-argument lists, preserved without
///     normalizing one form into the other.</item>
/// </list>
/// Each test parses a snippet in-process, pretty-prints the AST, and asserts the
/// emitted text matches an expected string exactly (line endings normalized to
/// <c>\n</c>). All parsing and printing runs in-process with zero external process
/// spawns.
/// </summary>
public class VariadicsPrettyPrinterTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    /// <summary>
    /// Parses the source, pretty-prints the AST, and normalizes line endings to
    /// <c>\n</c> so the exact-text comparison is platform-independent.
    /// </summary>
    private static string PrintNormalized(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source).Replace("\r\n", "\n");
    }

    // === Requirement 2.1: `params TArgs` preserves the declared pack identifier ===

    [Fact]
    public void ParamsPack_PrintsExactly_PreservingIdentifier()
    {
        var printed = PrintNormalized("class Foo<params CtorArgs> { }");

        Assert.Equal("class Foo<params CtorArgs> {\n}\n", printed);
    }

    // === Requirement 2.2: explicit `Type[]:TArgs` (unknown-length) domain ===

    [Fact]
    public void ExplicitUnsizedPack_PrintsExactly()
    {
        var printed = PrintNormalized("class Foo<Type[]:TArgs> { }");

        Assert.Equal("class Foo<Type[]:TArgs> {\n}\n", printed);
    }

    // === Requirement 2.2: fixed-size `Type[N]:TArgs` domain preserves N ===

    [Fact]
    public void ExplicitFixedSizePack_PrintsExactly_PreservingLength()
    {
        var printed = PrintNormalized("class Foo<Type[2]:TShapes> { }");

        Assert.Equal("class Foo<Type[2]:TShapes> {\n}\n", printed);
    }

    // === Requirement 2.3: a type-pack spread `TArgs...` in a member declaration ===

    [Fact]
    public void TypePackSpread_InFieldDeclaration_PrintsExactly()
    {
        var printed = PrintNormalized("class Bundle<params TArgs> { public TArgs... Item; }");

        Assert.Equal("class Bundle<params TArgs> {\n    public TArgs... Item;\n}\n", printed);
    }

    // === Requirement 2.3: a type-pack spread `TArgs...` in a type-argument list ===

    [Fact]
    public void TypePackSpread_InTypeArgumentList_PrintsExactly()
    {
        var printed = PrintNormalized("type Action<params TArgs> = Func<TArgs..., void>;");

        Assert.Equal("type Action<params TArgs> = Func<TArgs..., void>;\n", printed);
    }

    // === Requirement 2.3: a tuple-value spread `args...` in an argument list ===

    [Fact]
    public void TupleValueSpread_InArgumentList_PrintsExactly()
    {
        var printed = PrintNormalized(
            "class C<params TArgs> { void M(params TArgs args) { Consume(args...); } }");

        Assert.Equal(
            "class C<params TArgs> {\n    void M(params TArgs args) {\n        Consume(args...);\n    }\n}\n",
            printed);
    }

    // === Requirement 2.4: value-side `params TArgs name` (value pack) ===

    [Fact]
    public void ValuePackParameter_PrintsExactly()
    {
        var printed = PrintNormalized("class C<params TArgs> { void M(params TArgs args) { } }");

        Assert.Equal("class C<params TArgs> {\n    void M(params TArgs args) {\n    }\n}\n", printed);
    }

    // === Requirement 2.4: value-side `params T[] name` (homogeneous value-level params) ===

    [Fact]
    public void ValueLevelParamsParameter_PrintsExactly()
    {
        var printed = PrintNormalized("class C { void M(params string[] parts) { } }");

        Assert.Equal("class C {\n    void M(params string[] parts) {\n    }\n}\n", printed);
    }

    // === Requirement 2.5: a flat type-argument list stays flat (not normalized to brackets) ===

    [Fact]
    public void FlatTypeArgumentList_PrintsExactly_AndIsNotBracketed()
    {
        var printed = PrintNormalized("class C { Foo<int, string> f; }");

        Assert.Equal("class C {\n    Foo<int, string> f;\n}\n", printed);

        // The flat form must NOT be normalized into the bracketed pack form.
        Assert.DoesNotContain("[", printed);
    }

    // === Requirement 2.5: a bracketed pack list stays bracketed (not flattened) ===

    [Fact]
    public void BracketedPackTypeArgumentList_PrintsExactly_AndIsNotFlattened()
    {
        var printed = PrintNormalized("class C { Foo<[int, string]> f; }");

        Assert.Equal("class C {\n    Foo<[int, string]> f;\n}\n", printed);

        // The bracketed form must be preserved verbatim, not flattened to `Foo<int, string>`.
        Assert.Contains("Foo<[int, string]>", printed);
    }
}