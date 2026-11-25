using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Parsing;

[Trait("Tier", "3")]
public sealed partial class PrinterArmRoundTripTests
{
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    [InlineData("%")]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("<=")]
    [InlineData(">=")]
    [InlineData("&&")]
    [InlineData("||")]
    [InlineData("!")]
    public void AnOperatorDeclarationKeepsItsSymbol(string symbol)
    {
        AssertPrintsBackUnchanged($"class P {{ public static bool operator {symbol}(P a, P b) {{ return true; }} }}");
    }

    [Theory]
    [InlineData("class P { type Meters = new int; }")]
    [InlineData("class P { type Alias = int; }")]
    [InlineData("class P { public int A { get; public set => Store(value); } }")]
    [InlineData("class P { public int A { get; init { Store(value); } } }")]
    [InlineData("class P { public int A { static get; } }")]
    [InlineData("class P { int Go() with readonly ref { return 1; } }")]
    [InlineData("class P { void Run<T>() where T : owned { } }")]
    [InlineData("namespace N { extension(int self) { int Twice() { return self * 2; } } }")]
    [InlineData("using A; class P { } using B;")]
    public void ADeclarationFormPrintsBackUnchanged(string source)
    {
        AssertPrintsBackUnchanged(source);
    }

    [Theory]
    [InlineData("if (a) { x = 1; } else if (b) { x = 2; } else if (c) { x = 3; } else { x = 4; }")]
    [InlineData("if (a) { x = 1; } else if (b) { x = 2; }")]
    [InlineData("{ var y = 1; }")]
    [InlineData("readonly (int a, string b, bool c) = Get();")]
    [InlineData("foreach (var x in xs) { Use(x); }")]
    [InlineData("for (; i < 3; i = i + 1) { }")]
    public void AStatementFormPrintsBackUnchanged(string statement)
    {
        AssertPrintsBackUnchanged($"class P {{ void Run() {{ {statement} }} }}");
    }

    [Theory]
    [InlineData("a ?? b ?? c")]
    [InlineData("!a")]
    [InlineData("xs[..^1]")]
    [InlineData("xs[1..]")]
    [InlineData("Get()!")]
    [InlineData("a?.B")]
    [InlineData("a?.C<int>(1)")]
    [InlineData("(a + b) * c")]
    [InlineData("$\"a {b} c {d + 1}\"")]
    [InlineData("Make<int>(1)")]
    [InlineData("List<int>.Empty")]
    [InlineData("new List<int> { 1, 2 }")]
    [InlineData("new { }")]
    [InlineData("new unknown[3]")]
    [InlineData("a is int or string")]
    [InlineData("a is [1, .., 3]")]
    [InlineData("a is [_, .. var rest]")]
    [InlineData("a is []")]
    [InlineData("a is { A: 1, B: 2 }")]
    [InlineData("a is 1.5 or 'c'")]
    public void AnExpressionFormPrintsBackUnchanged(string expression)
    {
        AssertPrintsBackUnchanged($"class P {{ void Run() {{ var x = {expression}; }} }}");
    }

    private static void AssertPrintsBackUnchanged(string source)
    {
        var printed = MistPrettyPrinter.Print(Parse(source), source);

        Assert.Equal(source, Whitespace().Replace(printed, " ").Trim());
        Assert.Equal(printed, MistPrettyPrinter.Print(Parse(printed), printed));
    }

    private static Storm.Runtime.SyntaxNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var result = new MistParser().Parse(new MistLexer().Lex(new SourceText("probe.mist", source), diagnostics), diagnostics);

        Assert.DoesNotContain(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return Assert.IsAssignableFrom<Storm.Runtime.SyntaxNode>(result.Root);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}