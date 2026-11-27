using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class PureLambdaPrettyPrinterTests
{
    [Fact]
    public void PureExpressionLambdaPrintsModifierBeforeTypedParameterList()
    {
        var body = MistAstGenerator.MakeMultiplicative(
            MistAstGenerator.MakeIdentifierExpr("n"),
            MistPropertyGenerator.MakeIntLiteral("3"));
        var lambda = new ExpressionLambdaLambdaExpressionNode
        {
            Modifiers = [new PureLambdaModifierLambdaModifierNode()],
            Params = new ParenthesizedLambdaParamsLambdaParameterListNode
            {
                Params = new LambdaParamListNode
                {
                    LambdaParam = new TypedLambdaParamLambdaParamNode
                    {
                        Type = new TypeReferenceNode
                        {
                            First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
                        },
                        Name = MistTypeGenerator.MakeToken("n"),
                    },
                },
            },
            Body = body,
        };
        var declaration = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = MistTypeGenerator.MakeToken("tripler"),
            Initializer = new LambdaFormExpressionNode { Lambda = lambda },
        };
        var program = new CompilationUnitNode
        {
            Items =
            [
                new StatementItemTopLevelItemNode
                {
                    Statement = new VariableDeclarationStatementStatementNode { VariableDeclaration = declaration },
                },
            ],
        };

        var printed = MistPrettyPrinter.Print(program);

        Assert.Equal("var tripler = pure (int n) => n * 3;\n", printed.ReplaceLineEndings("\n"));
    }
}