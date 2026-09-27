using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

[Trait("Tier", "3")]
public sealed class PuritySyntaxTests
{
    [Theory]
    [InlineData("class C { pure int M() { return 1; } }", "PureModifierModifierNode")]
    [InlineData("class C { local int M() { return 1; } }", "LocalModifierModifierNode")]
    [InlineData("class C { local readonly int M() { return 1; } }", "LocalModifierModifierNode")]
    [InlineData("class C { readonly local int M() { return 1; } }", "LocalModifierModifierNode")]
    [InlineData("class C { pure readonly int M() { return 1; } }", "PureModifierModifierNode")]
    [InlineData("class C { pure int P { get { return 1; } } }", "PureModifierModifierNode")]
    [InlineData("class C { local int P { get { return 1; } } }", "LocalModifierModifierNode")]
    [InlineData("class C { local readonly int P { get { return 1; } } }", "LocalModifierModifierNode")]
    [InlineData("class C { pure int this[int i] { get { return i; } } }", "PureModifierModifierNode")]
    [InlineData("class C { local int this[int i] { get { return i; } } }", "LocalModifierModifierNode")]
    [InlineData("class C { local readonly int this[int i] { get { return i; } } }", "LocalModifierModifierNode")]
    [InlineData("pure class C { }", "PureModifierModifierNode")]
    [InlineData("local class C { }", "LocalModifierModifierNode")]
    [InlineData("local readonly class C { }", "LocalModifierModifierNode")]
    [InlineData("pure struct S { }", "PureModifierModifierNode")]
    [InlineData("local struct S { }", "LocalModifierModifierNode")]
    [InlineData("local readonly struct S { }", "LocalModifierModifierNode")]
    [InlineData("pure abstract A { }", "PureModifierModifierNode")]
    [InlineData("local abstract A { }", "LocalModifierModifierNode")]
    [InlineData("local readonly abstract A { }", "LocalModifierModifierNode")]
    [InlineData("class C { void M() { pure int F(int x) { return x; } } }", "PureModifierModifierNode")]
    [InlineData("class C { void M() { local int F(int x) { return x; } } }", "LocalModifierModifierNode")]
    [InlineData("class C { void M() { local readonly int F(int x) { return x; } } }", "LocalModifierModifierNode")]
    [InlineData("class C { pure C() { } }", "PureModifierModifierNode")]
    [InlineData("class C { public static pure C operator +(C a, C b) { return a; } }", "PureModifierModifierNode")]
    [InlineData("class C { public static pure implicit operator int(C c) { return 1; } }", "PureModifierModifierNode")]
    [InlineData("abstract A { pure int M(); }", "PureModifierModifierNode")]
    [InlineData("abstract A { local readonly int M(); }", "LocalModifierModifierNode")]
    [InlineData("intrinsic class C { intrinsic pure int M(); }", "PureModifierModifierNode")]
    [InlineData("intrinsic class C { intrinsic local void M(); }", "LocalModifierModifierNode")]
    public void SourceToAstToPrettyPrinter_PurityModifiersSurviveOnEveryDeclarationThatAdmitsThem(string source, string nodeTypeName)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        Assert.Contains(nodeTypeName.StartsWith("Pure", StringComparison.Ordinal) ? "pure" : "local", printed, StringComparison.Ordinal);
        Assert.Contains(Descendants(first), node => node.GetType().Name == nodeTypeName);
        Assert.Contains(Descendants(second), node => node.GetType().Name == nodeTypeName);
        Assert.Equal(Shape(first), Shape(second));
    }

    [Fact]
    public void SourceToAst_LocalReadonlyIsTwoModifiers()
    {
        var root = Parse("class C { local readonly int M() { return 1; } }");

        Assert.Contains(Descendants(root), node => node.GetType().Name == "LocalModifierModifierNode");
        Assert.Contains(Descendants(root), node => node.GetType().Name == "ReadonlyModifierModifierNode");
    }

    [Theory]
    [InlineData("class C { void M() { var f = pure n => n + 1; } }", "PureLambdaModifierLambdaModifierNode")]
    [InlineData("class C { void M() { var f = pure (int n) => { return n + 1; }; } }", "PureLambdaModifierLambdaModifierNode")]
    [InlineData("class C { void M() { Run(pure n => n + 1); } }", "PureLambdaModifierLambdaModifierNode")]
    [InlineData("class C { void M() { var f = local (int a) => a; } }", "LocalLambdaModifierLambdaModifierNode")]
    [InlineData("class C { void M() { var f = local a => { return a; }; } }", "LocalLambdaModifierLambdaModifierNode")]
    [InlineData("class C { void M() { Run(local (int a) => a); } }", "LocalLambdaModifierLambdaModifierNode")]
    public void SourceToAstToPrettyPrinter_LambdaModifiersSurvive(string source, string nodeTypeName)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        Assert.Contains(nodeTypeName.StartsWith("Pure", StringComparison.Ordinal) ? "pure" : "local", printed, StringComparison.Ordinal);
        Assert.Contains(Descendants(first), node => node.GetType().Name == nodeTypeName);
        Assert.DoesNotContain(Descendants(first), node => node.GetType().Name is "PureModifierModifierNode" or "LocalModifierModifierNode");
        Assert.Contains(Descendants(second), node => node.GetType().Name == nodeTypeName);
        Assert.Equal(Shape(first), Shape(second));
    }

    [Theory]
    [InlineData("class C { int M(pure func<int, int> f) { return f(1); } }", "FuncSingleTypeRefSingleTypeRefNode", "pure func<")]
    [InlineData("class C { void M() { pure func<int, int> f = n => n; } }", "FuncSingleTypeRefSingleTypeRefNode", "pure func<")]
    [InlineData("type Step = pure func<int, int>;", "FuncSingleTypeRefSingleTypeRefNode", "pure func<")]
    [InlineData("class C { void M(Box<pure func<int, int>> b) { } }", "FuncSingleTypeRefSingleTypeRefNode", "pure func<")]
    [InlineData("class C { void M(pure func<int, int>[] fs) { } }", "FuncSingleTypeRefSingleTypeRefNode", "pure func<")]
    [InlineData("class C { void M() { var fs = new pure func<int, int>[2]; } }", "FuncElementTypeRefElementTypeRefNode", "pure func<")]
    [InlineData("class C { void M(pure Func<int, int> f) { } }", "NamedSingleTypeRefSingleTypeRefNode", "pure Func<")]
    [InlineData("class C { void M(pure Action<int> f) { } }", "NamedSingleTypeRefSingleTypeRefNode", "pure Action<")]
    [InlineData("class C { void M(pure Step f) { } }", "NamedSingleTypeRefSingleTypeRefNode", "pure Step")]
    [InlineData("class C { void M() { pure Step s = n => n; } }", "NamedSingleTypeRefSingleTypeRefNode", "pure Step")]
    [InlineData("class C { void M(Box<pure Step> b) { } }", "NamedSingleTypeRefSingleTypeRefNode", "pure Step")]
    [InlineData("class C { void M() { var fs = new pure Step[2]; } }", "NamedElementTypeRefElementTypeRefNode", "pure Step")]
    public void SourceToAstToPrettyPrinter_PureBelongsToTheFunctionTypeWhereNoDeclarationCanTakeIt(string source, string nodeTypeName, string spelling)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        Assert.Contains(spelling, printed, StringComparison.Ordinal);
        Assert.Contains(Descendants(first), node => node.GetType().Name == nodeTypeName);
        Assert.DoesNotContain(Descendants(first), node => node.GetType().Name == "PureModifierModifierNode");
        Assert.Contains(Descendants(second), node => node.GetType().Name == nodeTypeName);
        Assert.DoesNotContain(Descendants(second), node => node.GetType().Name == "PureModifierModifierNode");
        Assert.Equal(Shape(first), Shape(second));
    }

    [Theory]
    [InlineData("class C { pure func<int, int> Make() { return null; } }")]
    [InlineData("class C { void M() { pure func<int, int> Make() { return null; } } }")]
    [InlineData("class C { pure func<int, int> Step { get { return null; } } }")]
    [InlineData("class C { pure func<int, int> this[int i] { get { return null; } } }")]
    public void SourceToAstToPrettyPrinter_PureBeforeADeclarationsFunctionTypeIsTheDeclarationsModifier(string source)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        Assert.Contains(Descendants(first), node => node.GetType().Name == "PureModifierModifierNode");
        Assert.Contains(Descendants(first), node => node.GetType().Name == "FuncSingleTypeRefSingleTypeRefNode");
        Assert.Equal(1, Occurrences(printed, "pure"));
        Assert.Contains(Descendants(second), node => node.GetType().Name == "PureModifierModifierNode");
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

    private static int Occurrences(string text, string word)
    {
        var count = 0;
        var at = text.IndexOf(word, StringComparison.Ordinal);

        while(at >= 0)
        {
            count++;
            at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal);
        }

        return count;
    }
}