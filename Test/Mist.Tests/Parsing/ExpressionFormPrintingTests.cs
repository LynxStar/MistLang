using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

[Trait("Tier", "3")]
public sealed class ExpressionFormPrintingTests
{
    [Fact]
    public void AnExpressionBodiedInitAccessorPrintsBackAsOne()
    {
        const string source = "class Probe { public int Count { get; private init => Store(value); } }";

        var printed = MistPrettyPrinter.Print(Parse(source), source);
        var accessor = Assert.Single(Descendants(Parse(printed)).OfType<ExpressionInitAccessorAccessorDeclarationNode>());

        Assert.Contains("private init => Store(value);", printed, StringComparison.Ordinal);
        _ = Assert.IsType<PrivateModifierModifierNode>(accessor.AccessorModifier);
    }

    [Theory]
    [InlineData("i = 0", "i < 3")]
    [InlineData("Reset()", "")]
    public void AnExpressionForInitializerPrintsInTheInitializerSlot(string initializer, string condition)
    {
        var source = $"class Probe {{ void Run() {{ for({initializer}; {condition}; i = i + 1) {{ }} }} }}";

        var printed = MistPrettyPrinter.Print(Parse(source), source);
        var loop = Assert.Single(Descendants(Parse(printed)).OfType<ForStatementNode>());

        Assert.Contains($"for ({initializer}; {condition}; i = i + 1)", printed, StringComparison.Ordinal);
        _ = Assert.IsType<ForInitializerExpressionForInitializerNode>(loop.Initializer);
        Assert.Equal(condition.Length == 0, loop.Condition is null);
    }

    private static Storm.Runtime.SyntaxNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var result = new MistParser().Parse(new MistLexer().Lex(new SourceText("probe.mist", source), diagnostics), diagnostics);

        Assert.DoesNotContain(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return Assert.IsAssignableFrom<Storm.Runtime.SyntaxNode>(result.Root);
    }

    private static IEnumerable<Storm.Runtime.SyntaxNode> Descendants(Storm.Runtime.SyntaxNode node)
    {
        foreach(var child in node.Children().Where(child => child is not null))
        {
            yield return child;

            foreach(var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}