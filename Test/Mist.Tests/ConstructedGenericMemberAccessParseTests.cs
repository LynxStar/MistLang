// Tier 1 (Unit): the parser alone. A member named through an instantiation, `Task<int>.WhenAll(x)`, is
// read as one primary rather than as a comparison chain, and a comparison or a generic call stays what
// it was. Nothing past the parser runs.

using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Tests;

public sealed class ConstructedGenericMemberAccessParseTests
{
    private static Storm.Runtime.SyntaxNode Parse(string statement)
    {
        var source = new SourceText(
            "probe.mist", $"public class Probe {{ public static void Run() {{ {statement} }} }}");
        var diagnostics = new DiagnosticBag();
        var tokens = new MistLexer().Lex(source, diagnostics);
        var result = new MistParser().Parse(tokens, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(result.Root);
        return result.Root;
    }

    private static IEnumerable<Storm.Runtime.SyntaxNode> Descendants(Storm.Runtime.SyntaxNode node)
    {
        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            yield return child;

            foreach(var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    [Theory]
    [InlineData("var all = Task<int>.WhenAll(pending);", "Task", "WhenAll")]
    [InlineData("var info = Box<int>.Wrapper.Info();", "Box", "Wrapper")]
    [InlineData("var later = Standard.Task<int>.Delay(1);", "Task", "Delay")]
    [InlineData("var pair = Pair<int, string>.Empty;", "Pair", "Empty")]
    public void AMemberThroughAnInstantiationIsOnePrimary(string statement, string type, string member)
    {
        var access = Assert.Single(Descendants(Parse(statement)).OfType<Generated.GenericTypeMemberAccessExpressionPrimaryNode>());
        var lastSegment = access.Type.IDENTIFIERs.Length > 0 ? access.Type.IDENTIFIERs[^1] : access.Type.IDENTIFIER;

        Assert.Equal(type, lastSegment.Lexeme);
        Assert.Equal(member, access.Member.Lexeme);
    }

    [Theory]
    [InlineData("var less = a < b;")]
    [InlineData("var both = a < b && c > d;")]
    [InlineData("var made = Make<int>(1);")]
    [InlineData("var plain = Task.Delay(1);")]
    public void AComparisonOrAGenericCallIsNotOne(string statement)
    {
        Assert.Empty(Descendants(Parse(statement)).OfType<Generated.GenericTypeMemberAccessExpressionPrimaryNode>());
    }
}