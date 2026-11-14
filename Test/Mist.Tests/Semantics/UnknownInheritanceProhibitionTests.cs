// Feature: unknown-type. Validates Requirements 6.1, 6.2, 6.3, 6.4, 16.6.
//
// `unknown` lexes to the reserved UNKNOWN keyword token and a base names only IDENTIFIER tokens, so
// naming it as a base never parses. Each case is paired with a legal identifier in the same
// position, so the failure is attributable to `unknown` alone.

using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Semantics;

[Trait("Tier", "3")]
public class UnknownInheritanceProhibitionTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static (CompilationUnitNode? Root, bool HasErrors) ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        return (result.Root as CompilationUnitNode, hasErrors);
    }

    [Fact]
    public void ClassBaseClauseWithUnknown_FailsToParse()
    {
        var (cuInvalid, _) = ParseSource("class Foo : unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class Bar { } class Foo : Bar { }");
        Assert.NotNull(cuValid);
        Assert.Equal(2, cuValid!.GetTypes().Length);
    }

    [Fact]
    public void StructAbstractListWithUnknown_FailsToParse()
    {
        var (cuInvalid, _) = ParseSource("struct S : unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("abstract I { } struct S : I { }");
        Assert.NotNull(cuValid);
        Assert.Equal(2, cuValid!.GetTypes().Length);
    }

    [Fact]
    public void AbstractBaseListWithUnknown_FailsToParse()
    {
        var (cuInvalid, _) = ParseSource("abstract I : unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("abstract J { } abstract I : J { }");
        Assert.NotNull(cuValid);
        Assert.Equal(2, cuValid!.GetTypes().Length);
    }
}