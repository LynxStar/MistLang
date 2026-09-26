using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

public sealed class StampableSyntaxTests
{
    [Theory]
    [InlineData("public stampable struct Value { public int Item; }")]
    [InlineData("public stampable struct Value<T> where T : stampable { public T Item; }")]
    [InlineData("class Factory { public static void Make<T>() where T : stampable { } }")]
    [InlineData("abstract Factory { type Item : stampable; }")]
    public void SourceToAstToPrettyPrinter_StampableSyntaxPreservesItsKeywordRole(string source)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        Assert.Contains("stampable", printed, StringComparison.Ordinal);
        Assert.Contains(Descendants(first), node => node.GetType().Name is "StampableModifierModifierNode" or "StampableConstraintConstraintNode");
        Assert.Equal(Shape(first), Shape(second));
    }

    [Fact]
    public void SourceToAst_StampableConstraintIsAKeywordRatherThanANamedType()
    {
        var root = Parse("struct Value<T> where T : stampable { public T Item; }");

        Assert.Contains(Descendants(root), node => node.GetType().Name == "StampableConstraintConstraintNode");
        Assert.DoesNotContain(Descendants(root), node => node is TypeConstraintConstraintNode);
    }

    private static CompilationUnitNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var text = new SourceText("probe.mist", source);
        var result = new MistParser().Parse(new MistLexer().Lex(text, diagnostics), diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static IEnumerable<Storm.Runtime.SyntaxNode> Descendants(Storm.Runtime.SyntaxNode node)
    {
        yield return node;

        foreach(var child in node.Children().Where(child => child is not null))
        {
            foreach(var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static string Shape(Storm.Runtime.SyntaxNode node)
    {
        return $"{node.GetType().Name}({string.Join(",", node.Children().Where(child => child is not null).Select(Shape))})";
    }
}