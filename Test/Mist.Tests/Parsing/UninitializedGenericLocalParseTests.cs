using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class UninitializedGenericLocalParseTests
{
    [Theory]
    [InlineData("List<int> s;", "s")]
    [InlineData("Span<int> s;", "s")]
    [InlineData("Dictionary<string, int> d;", "d")]
    [InlineData("owned List<int> s;", "s")]
    [InlineData("List<List<int>> nested;", "nested")]
    public void AGenericLocalWithoutAnInitializerIsADeclaration(string statement, string name)
    {
        var declaration = Assert.IsType<UninitializedVariableStatementStatementNode>(ParseStatement(statement));

        Assert.Equal(name, declaration.UninitializedVariable.Name.Lexeme);
        var type = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(declaration.UninitializedVariable.Type.First);
        Assert.NotNull(type.TypeArgs);
    }

    [Fact]
    public void TheOwnershipSpecifierStaysOnTheGenericDeclaration()
    {
        var declaration = Assert.IsType<UninitializedVariableStatementStatementNode>(ParseStatement("owned List<int> s;"));

        _ = Assert.IsType<OwnedSpecifierOwnershipSpecifierNode>(declaration.UninitializedVariable.OwnershipSpecifier);
    }

    [Theory]
    [InlineData("Holder h;")]
    [InlineData("(int, string) pair;")]
    public void ANonGenericOrTupleLocalStaysADeclaration(string statement)
    {
        _ = Assert.IsType<UninitializedVariableStatementStatementNode>(ParseStatement(statement));
    }

    [Theory]
    [InlineData("(ulong)a;")]
    [InlineData("(Holder)a;")]
    public void ACastStatementStaysACast(string statement)
    {
        var expression = Assert.IsType<ExpressionStatementStatementNode>(ParseStatement(statement));

        _ = Assert.Single(Descendants(expression).OfType<CastExpressionUnaryNode>());
    }

    [Theory]
    [InlineData("Foo<int>(x);")]
    [InlineData("x = 1;")]
    [InlineData("s = new List<int>();")]
    [InlineData("Console.WriteLine(s.Count);")]
    public void AnInvocationOrAssignmentStaysAnExpressionStatement(string statement)
    {
        _ = Assert.IsType<ExpressionStatementStatementNode>(ParseStatement(statement));
    }

    private static StatementNode ParseStatement(string statement)
    {
        var source = new SourceText("probe.mist", $"public class Probe {{ public static void Run() {{ {statement} }} }}");
        var diagnostics = new DiagnosticBag();
        var result = new Mist.Parsing.MistParser().Parse(new Mist.Lexing.MistLexer().Lex(source, diagnostics), diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        var unit = Assert.IsType<CompilationUnitNode>(result.Root);
        var type = (BodyTypeTypeDeclarationNode)unit.GetTypes()[0];
        var method = ((MethodMemberMemberDeclarationNode)type.TypeBody.Members[0]).Method;

        return Assert.Single(method.Body.GetBlock()!.Statements);
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