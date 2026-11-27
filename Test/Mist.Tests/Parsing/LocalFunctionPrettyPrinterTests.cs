using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class LocalFunctionPrettyPrinterTests
{
    [Fact]
    public void StaticLocalFunctionPreservesModifierAndReturnOperand()
    {
        var returnStatement = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = MistPropertyGenerator.MakeIntLiteral("42") },
        };
        var localFunction = new LocalFunctionDeclarationNode
        {
            Modifiers = [new StaticModifierModifierNode()],
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            },
            Name = MistTypeGenerator.MakeToken("GetAnswer"),
            Body = new BlockNode { Statements = [returnStatement] },
        };
        var program = new CompilationUnitNode
        {
            Items =
            [
                new StatementItemTopLevelItemNode
                {
                    Statement = new LocalFunctionDeclarationStatementStatementNode { LocalFunction = localFunction },
                },
            ],
        };

        var printed = MistPrettyPrinter.Print(program);

        Assert.Equal("static int GetAnswer() {\n    return 42;\n}\n", printed.ReplaceLineEndings("\n"));
    }
}