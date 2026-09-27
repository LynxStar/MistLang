// Purity Requirement 1.5 and 1.7 at the accessor: an accessor may state its own promise ahead of its
// accessibility, on a property and on an indexer, auto or bodied, and the pretty printer keeps it, so
// parse, print and parse again agree.

using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

public sealed class AccessorPromiseSyntaxTests
{
    private static readonly string[] Spellings =
    [
        "local readonly get", "local set", "pure get", "local private set", "private set",
    ];

    private const string LocalReadonlyPromise = "LocalReadonlyAccessorPromiseAccessorPromiseNode";
    private const string LocalPromise = "LocalAccessorPromiseAccessorPromiseNode";
    private const string PurePromise = "PureAccessorPromiseAccessorPromiseNode";
    private const string PrivateModifier = "PrivateModifierModifierNode";

    [Theory]
    [InlineData("class C { int P { local readonly get; local set; } }", LocalReadonlyPromise, LocalPromise)]
    [InlineData("class C { int P { pure get; } }", PurePromise)]
    [InlineData("class C { int P { local set; } }", LocalPromise)]
    [InlineData("class C { int P { local readonly get { return 1; } local set { } } }", LocalReadonlyPromise, LocalPromise)]
    [InlineData("class C { int P { pure get { return 1; } } }", PurePromise)]
    [InlineData("class C { int this[int i] { local readonly get; local set; } }", LocalReadonlyPromise, LocalPromise)]
    [InlineData("class C { int this[int i] { pure get; } }", PurePromise)]
    [InlineData("class C { int this[int i] { local set; } }", LocalPromise)]
    [InlineData("class C { int this[int i] { local readonly get { return i; } local set { } } }", LocalReadonlyPromise, LocalPromise)]
    [InlineData("class C { int P { local readonly get; local private set; } }", LocalReadonlyPromise, LocalPromise, PrivateModifier)]
    [InlineData("abstract A { int P { local readonly get; local set; } }", LocalReadonlyPromise, LocalPromise)]
    [InlineData("intrinsic class L { intrinsic int this[int i] { local readonly get; local set; } }", LocalReadonlyPromise, LocalPromise)]
    [InlineData("class C { int P { get; private set; } }", PrivateModifier)]
    public void SourceToAstToPrettyPrinter_AnAccessorsOwnPromiseSurvives(string source, params string[] nodeTypeNames)
    {
        var first = Parse(source);
        var printed = MistPrettyPrinter.Print(first, source);
        var second = Parse(printed);

        foreach(var spelling in Spellings.Where(spelling => source.Contains(spelling, StringComparison.Ordinal)))
        {
            Assert.Contains(spelling, printed, StringComparison.Ordinal);
        }

        foreach(var nodeTypeName in nodeTypeNames)
        {
            Assert.Contains(Descendants(first), node => node.GetType().Name == nodeTypeName);
            Assert.Contains(Descendants(second), node => node.GetType().Name == nodeTypeName);
        }

        Assert.Equal(Shape(first), Shape(second));
    }

    [Fact]
    public void SourceToAst_AnAccessorsLocalReadonlyIsOnePromiseOnTheAccessorAndNoModifierOnTheDeclaration()
    {
        var root = Parse("class C { int P { local readonly get; set; } }");

        Assert.Equal(1, Descendants(root).Count(node => node.GetType().Name == LocalReadonlyPromise));
        Assert.DoesNotContain(Descendants(root), node => node.GetType().Name is "LocalModifierModifierNode" or "ReadonlyModifierModifierNode");
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