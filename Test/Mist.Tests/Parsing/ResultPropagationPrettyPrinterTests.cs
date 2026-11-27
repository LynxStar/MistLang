using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class ResultPropagationPrettyPrinterTests
{
    [Fact]
    public void PrintsPropagationBetweenInvocationAndMemberAccess()
    {
        var expression = BindingFragments.Expression(
            BindingFragments.Identifier("Read"),
            new InvocationSuffixPostfixSuffixNode(),
            new PropagationSuffixPostfixSuffixNode(),
            new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token("Length") });
        var program = new CompilationUnitNode
        {
            Items = [new StatementItemTopLevelItemNode { Statement = MistAstGenerator.MakeReturnStatement(expression) }],
        };

        var printed = MistPrettyPrinter.Print(program);

        Assert.Equal("return Read()!.Length;" + Environment.NewLine, printed);
    }
}