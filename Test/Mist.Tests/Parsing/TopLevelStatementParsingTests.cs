using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Verifies that top-level statements parse correctly and produce
/// StatementItemTopLevelItemNode entries in the CompilationUnitNode.
/// </summary>
public class TopLevelStatementParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    [Fact]
    public void VarDeclaration_AtFileScope_ParsesWithoutErrors()
    {
        var result = ParseSource("var x = 1;");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void VarDeclaration_AtFileScope_ProducesStatementItemTopLevelItemNode()
    {
        var result = ParseSource("var x = 1;");

        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.NotEmpty(cu.Items);
        Assert.Contains(cu.Items, item => item is StatementItemTopLevelItemNode);
    }

    [Fact]
    public void VarDeclaration_AtFileScope_HasStatementProperty()
    {
        var result = ParseSource("var x = 1;");

        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        var statementItem = Assert.IsType<StatementItemTopLevelItemNode>(cu.Items[0]);
        Assert.NotNull(statementItem.Statement);
    }

    [Fact]
    public void MultipleStatements_AtFileScope_AllParseAsStatementItems()
    {
        var source = """
            var x = 1;
            var y = 2;
            var z = x;
            """;

        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Equal(3, cu.Items.Length);
        Assert.All(cu.Items, item => Assert.IsType<StatementItemTopLevelItemNode>(item));
    }

    [Fact]
    public void StatementsAndTypeDeclarations_CoexistInSameFile()
    {
        var source = """
            var x = 1;
            class Foo { }
            """;

        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Equal(2, cu.Items.Length);
        Assert.IsType<StatementItemTopLevelItemNode>(cu.Items[0]);
        Assert.IsType<TypeItemTopLevelItemNode>(cu.Items[1]);
    }
}
