using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class GenericMethodGroupPrettyPrinterTests
{
    [Fact]
    public void QualifiedMethodGroupPrintsExplicitTypeArgumentsInOrderWithoutInvocation()
    {
        var receiver = new IdentifierExpressionPrimaryNode { Name = MistTypeGenerator.MakeToken("Api") };
        var suffix = new GenericMethodGroupSuffixPostfixSuffixNode
        {
            Name = MistTypeGenerator.MakeToken("Pick"),
            TypeArgs = new TypeArgumentListNode
            {
                TypeArgument = TypeArgument(new IntTypePrimitiveTypeNode()),
                TypeArguments = [TypeArgument(new StringTypePrimitiveTypeNode())],
            },
        };
        var declaration = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = MistTypeGenerator.MakeToken("pick"),
            Initializer = BindingFragments.Expression(receiver, suffix),
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

        Assert.Equal("var pick = Api.Pick<int, string>;\n", printed.ReplaceLineEndings("\n"));
    }

    private static TypeArgumentNode TypeArgument(PrimitiveTypeNode primitive)
    {
        return new TypeReferenceTypeArgumentTypeArgumentNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitive },
            },
        };
    }
}