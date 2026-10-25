using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AsyncModifierPlacementTests
{
    [Theory]
    [InlineData("operator")]
    [InlineData("implicit-cast-operator")]
    [InlineData("explicit-cast-operator")]
    [InlineData("constructor")]
    [InlineData("field")]
    [InlineData("block-property")]
    [InlineData("expression-bodied-property")]
    [InlineData("indexer")]
    public void AsyncOnADeclarationThatIsNotAMethodIsRefused(string declaration)
    {
        var f = new BindingFragments();

        _ = Declare(f, Member(declaration));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_ASYNC_NOT_ALLOWED", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AsyncOnAMethodIsAdmitted()
    {
        var f = new BindingFragments();

        var host = Declare(f, Member("method"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.True(Assert.Single(host.MethodsList).Modifiers.IsAsync);
    }

    private static ClassType Declare(BindingFragments f, MemberDeclarationNode member)
    {
        var body = BindingFragments.At<TypeBodyDeclarationNode>(10);
        body.Name = BindingFragments.Token("Host");
        body.Keyword = new ClassKeywordTypeKeywordNode();
        body.Members = [member];

        var host = new ClassType("Host", "Probe", f.Types, null, new BodyTypeTypeDeclarationNode { TypeBody = body });
        new ConcreteTypeBuilder(f.Builder, f.Builder.Resolver).Build(host);
        return host;
    }

    private static MemberDeclarationNode Member(string declaration)
    {
        ModifierNode[] written = [new AsyncModifierModifierNode()];
        ModifierNode[] onOperator = [new StaticModifierModifierNode(), .. written];

        switch(declaration)
        {
            case "operator":
                var op = BindingFragments.At<OperatorDeclarationNode>();
                op.Modifiers = [.. onOperator];
                op.ReturnType = BindingFragments.TypeName("Int32");
                op.Symbol = new PlusOpOperatorSymbolNode();
                op.Parameters = Parameters("left", "right");
                op.Body = Body();
                return new OperatorMemberMemberDeclarationNode { Operator = op };

            case "implicit-cast-operator":
            case "explicit-cast-operator":
                var cast = BindingFragments.At<CastOperatorDeclarationNode>();
                cast.Modifiers = [.. onOperator];
                cast.CastKind = declaration == "implicit-cast-operator" ? new ImplicitCastCastOperatorKindNode() : new ExplicitCastCastOperatorKindNode();
                cast.TargetType = BindingFragments.TypeName("Int32");
                cast.Parameters = Parameters("value");
                cast.Body = Body();
                return new CastOperatorMemberMemberDeclarationNode { CastOperator = cast };

            case "constructor":
                var constructor = BindingFragments.At<ConstructorDeclarationNode>();
                constructor.Modifiers = [.. written];
                constructor.Name = BindingFragments.Token("Host");
                constructor.Body = Body();
                return new ConstructorMemberMemberDeclarationNode { Constructor = constructor };

            case "field":
                var field = BindingFragments.At<FieldDeclarationNode>();
                field.Modifiers = [.. written];
                field.Type = BindingFragments.TypeName("Int32");
                field.Name = BindingFragments.Token("count");
                return new FieldMemberMemberDeclarationNode { Field = field };

            case "block-property":
                var blockProperty = BindingFragments.At<BlockPropertyPropertyDeclarationNode>();
                blockProperty.Modifiers = [.. written];
                blockProperty.Type = BindingFragments.TypeName("Int32");
                blockProperty.Name = BindingFragments.Token("Count");
                blockProperty.Accessors = [new AutoGetAccessorAccessorDeclarationNode()];
                return new PropertyMemberMemberDeclarationNode { Property = blockProperty };

            case "expression-bodied-property":
                var expressionProperty = BindingFragments.At<ExpressionBodiedPropertyPropertyDeclarationNode>();
                expressionProperty.Modifiers = [.. written];
                expressionProperty.Type = BindingFragments.TypeName("Int32");
                expressionProperty.Name = BindingFragments.Token("Count");
                expressionProperty.Body = BindingFragments.Expression(BindingFragments.Identifier("count"));
                return new PropertyMemberMemberDeclarationNode { Property = expressionProperty };

            case "indexer":
                var indexer = BindingFragments.At<IndexerDeclarationNode>();
                indexer.Modifiers = [.. written];
                indexer.Type = BindingFragments.TypeName("Int32");
                indexer.Parameters = Parameters("slot");
                indexer.Body = new BlockIndexerBodyIndexerBodyNode
                {
                    Accessors = [new BlockGetAccessorAccessorDeclarationNode { Body = new BlockNode() }],
                };
                return new IndexerMemberMemberDeclarationNode { Indexer = indexer };

            case "method":
                var method = BindingFragments.At<MethodDeclarationNode>();
                method.Modifiers = [.. written];
                method.ReturnType = BindingFragments.TypeName("Int32");
                method.Name = BindingFragments.Token("Compute");
                method.Body = Body();
                return new MethodMemberMemberDeclarationNode { Method = method };

            default:
                throw new ArgumentException("Unknown declaration", nameof(declaration));
        }
    }

    private static ParameterListNode Parameters(params string[] names)
    {
        var parameters = names
            .Select(name => new ParameterNode { Type = BindingFragments.TypeName("Int32"), Name = BindingFragments.Token(name) })
            .ToList();

        return new ParameterListNode { Parameter = parameters[0], Parameters = [.. parameters.Skip(1)] };
    }

    private static CallableBodyNode Body()
    {
        return new CallableBlockBodyCallableBodyNode { Block = new BlockNode() };
    }
}