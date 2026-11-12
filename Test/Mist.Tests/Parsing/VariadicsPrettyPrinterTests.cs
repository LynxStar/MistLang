// Feature: variadics - Unit tests for pretty-printer handling of variadic surface forms (Task 2.3)
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

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

    private static string PrintNormalized(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source).Replace("\r\n", "\n");
    }

    [Fact]
    public void ParamsPack_PrintsExactly_PreservingIdentifier()
    {
        var printed = PrintNormalized("class Foo<params CtorArgs> { }");

        Assert.Equal("class Foo<params CtorArgs> {\n}\n", printed);
    }

    [Fact]
    public void ExplicitUnsizedPack_PrintsExactly()
    {
        var printed = PrintNormalized("class Foo<Type[]:TArgs> { }");

        Assert.Equal("class Foo<Type[]:TArgs> {\n}\n", printed);
    }

    [Fact]
    public void ExplicitFixedSizePack_PrintsExactly_PreservingLength()
    {
        var printed = PrintNormalized("class Foo<Type[2]:TShapes> { }");

        Assert.Equal("class Foo<Type[2]:TShapes> {\n}\n", printed);
    }

    [Fact]
    public void TypePackSpread_InFieldDeclaration_PrintsExactly()
    {
        var printed = PrintNormalized("class Bundle<params TArgs> { public TArgs... Item; }");

        Assert.Equal("class Bundle<params TArgs> {\n    public TArgs... Item;\n}\n", printed);
    }

    [Fact]
    public void TypePackSpread_InTypeArgumentList_PrintsExactly()
    {
        var printed = PrintNormalized("type Action<params TArgs> = Func<TArgs..., void>;");

        Assert.Equal("type Action<params TArgs> = Func<TArgs..., void>;\n", printed);
    }

    [Fact]
    public void TupleValueSpread_InArgumentList_PrintsExactly()
    {
        var printed = PrintNormalized(
            "class C<params TArgs> { void M(params TArgs args) { Consume(args...); } }");

        Assert.Equal(
            "class C<params TArgs> {\n    void M(params TArgs args) {\n        Consume(args...);\n    }\n}\n",
            printed);
    }

    [Fact]
    public void ValuePackParameter_PrintsExactly()
    {
        var printed = PrintNormalized("class C<params TArgs> { void M(params TArgs args) { } }");

        Assert.Equal("class C<params TArgs> {\n    void M(params TArgs args) {\n    }\n}\n", printed);
    }

    [Fact]
    public void ValueLevelParamsParameter_PrintsExactly()
    {
        var printed = PrintNormalized("class C { void M(params string[] parts) { } }");

        Assert.Equal("class C {\n    void M(params string[] parts) {\n    }\n}\n", printed);
    }

    [Fact]
    public void FlatTypeArgumentList_PrintsExactly_AndIsNotBracketed()
    {
        var printed = PrintNormalized("class C { Foo<int, string> f; }");

        Assert.Equal("class C {\n    Foo<int, string> f;\n}\n", printed);

        Assert.DoesNotContain("[", printed);
    }

    [Fact]
    public void BracketedPackTypeArgumentList_PrintsExactly_AndIsNotFlattened()
    {
        var printed = PrintNormalized("class C { Foo<[int, string]> f; }");

        Assert.Equal("class C {\n    Foo<[int, string]> f;\n}\n", printed);

        Assert.Contains("Foo<[int, string]>", printed);
    }
}