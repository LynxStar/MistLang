using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class LogicalChainPrettyPrinterTests
{
    [Fact]
    public void EveryOperandOfALogicalChainIsPrinted()
    {
        var or = MistAstGenerator.MakeLogicalOr(MistAstGenerator.MakeIdentifierExpr("a"), MistAstGenerator.MakeIdentifierExpr("b"));
        var orNode = or.AsAssignment().Target.Left;
        orNode.LogicalAnds = orNode.LogicalAnds.Add(LogicalOrOf(MistAstGenerator.MakeIdentifierExpr("c")).Left);
        var and = MistAstGenerator.MakeLogicalAnd(MistAstGenerator.MakeIdentifierExpr("d"), MistAstGenerator.MakeIdentifierExpr("e"));
        var andNode = LogicalOrOf(and).Left;
        andNode.Equalitys = andNode.Equalitys.Add(LogicalOrOf(MistAstGenerator.MakeIdentifierExpr("f")).Left.Left);
        var program = MistProgramGenerator.MakeProgram([MistAstGenerator.MakeExpressionStatement(or), MistAstGenerator.MakeExpressionStatement(and)]);

        var printed = MistPrettyPrinter.Print(program);

        Assert.Contains("a || b || c;", printed);
        Assert.Contains("d && e && f;", printed);
    }

    private static LogicalOrNode LogicalOrOf(ExpressionNode expression)
    {
        return expression.AsAssignment().Target.Left;
    }
}