using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

[Trait("Tier", "3")]
public sealed class OptionalSuffixPrintingTests
{
    [Theory]
    [InlineData("int?")]
    [InlineData("int[]?")]
    [InlineData("int[3]?")]
    [InlineData("Widget?")]
    [InlineData("List<int>?")]
    [InlineData("Widget[]?")]
    [InlineData("Box<int>.Wrapper?")]
    [InlineData("Box<int>.Wrapper[]?")]
    [InlineData("func<int, int>?")]
    [InlineData("(func<int, int>)?")]
    [InlineData("int? | string")]
    public void TheOptionalSuffixSurvivesPrinting(string type)
    {
        var source = $"class Probe {{ void Run({type} value) {{ }} }}";

        var printed = MistPrettyPrinter.Print(Parse(source), source);

        Assert.Contains($"({type} value)", printed, StringComparison.Ordinal);
    }

    private static Storm.Runtime.SyntaxNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var result = new MistParser().Parse(new MistLexer().Lex(new SourceText("probe.mist", source), diagnostics), diagnostics);

        Assert.DoesNotContain(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return Assert.IsAssignableFrom<Storm.Runtime.SyntaxNode>(result.Root);
    }
}