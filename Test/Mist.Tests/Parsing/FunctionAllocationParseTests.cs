using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

public sealed class FunctionAllocationParseTests
{
    [Theory]
    [InlineData("new func<int, int>[2]")]
    [InlineData("new func<void>[Size()]")]
    [InlineData("stackalloc func<int, int>[2]")]
    [InlineData("stackalloc func<void>[Size()]")]
    public void SourceToAstToPrettyPrinter_FunctionElementsKeepTheAllocationSize(string allocation)
    {
        var source = $"class Probe {{ void Run() {{ var items = {allocation}; }} }}";
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        Assert.Contains(allocation, printed, StringComparison.Ordinal);
        Assert.Equal(Shape(first), Shape(second));
    }

    private static CompilationUnitNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var text = new SourceText("probe.mist", source);
        var result = new MistParser().Parse(new MistLexer().Lex(text, diagnostics), diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static string Shape(Storm.Runtime.SyntaxNode node)
    {
        return $"{node.GetType().Name}({string.Join(",", node.Children().Where(child => child is not null).Select(Shape))})";
    }
}