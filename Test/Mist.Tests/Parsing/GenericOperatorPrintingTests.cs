using System.Collections.Immutable;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class GenericOperatorPrintingTests
{
    [Fact]
    public void OperatorTypeParametersSurviveFormatting()
    {
        var declaration = new BodyTypeTypeDeclarationNode
        {
            TypeBody = new TypeBodyDeclarationNode
            {
                Name = BindingFragments.Token("Number"),
                Keyword = new StructKeywordTypeKeywordNode(),
                Members = [new OperatorMemberMemberDeclarationNode
                {
                    Operator = new OperatorDeclarationNode
                    {
                        ReturnType = BindingFragments.TypeName("Number"), Symbol = new PlusOpOperatorSymbolNode(),
                        TypeParams = new TypeParameterListNode
                        {
                            TypeParameter = new RegularTypeParameterTypeParameterNode { Name = BindingFragments.Token("E") },
                        },
                        Body = new CallableNoBodyCallableBodyNode(),
                    },
                }],
            },
        };
        var unit = new CompilationUnitNode { Items = new TypeDeclarationNode[] { declaration }.ToImmutableArray().AsTopLevelItems() };
        Assert.Contains("operator +<E>()", MistPrettyPrinter.Print(unit));
    }
}