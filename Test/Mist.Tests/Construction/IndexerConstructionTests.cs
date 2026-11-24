using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class IndexerConstructionTests
{
    private readonly BindingFragments f = new();

    [Fact]
    public void AnAutoAccessorOnAConcreteTypesIndexerIsRefusedAtEachAccessor()
    {
        var indexer = Build(Class(), [], BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(40), BindingFragments.At<AutoSetAccessorAccessorDeclarationNode>(50));

        var diagnostics = f.Diagnostics.ToImmutableArray();
        Assert.Equal(["MIST0330", "MIST0330"], diagnostics.Select(diagnostic => diagnostic.Number.Value));
        Assert.Equal([41, 51], diagnostics.Select(diagnostic => diagnostic.Position().Start.Column));
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);
    }

    [Theory]
    [InlineData("abstract")]
    [InlineData("intrinsic")]
    public void AutoAccessorsAreAdmittedOnAnAbstractOrIntrinsicIndexer(string kind)
    {
        MemberType host = kind == "abstract" ? new AbstractType("Shape", "Probe", f.Types, null, null) : Class(intrinsic: true);
        ModifierNode[] modifiers = kind == "abstract" ? [] : [new PublicModifierModifierNode(), new IntrinsicModifierModifierNode()];

        var indexer = Build(host, modifiers, BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(40), BindingFragments.At<AutoSetAccessorAccessorDeclarationNode>(50));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.True(indexer.Getter!.IsAuto);
        Assert.True(indexer.Setter!.IsAuto);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("block")]
    [InlineData("expression")]
    public void AnInitAccessorOnAnIndexerIsRefusedAtTheIndexerAndBuildsNoSetter(string form)
    {
        var indexer = Build(Class(), [], BlockGet(40), Init(form, 50));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0312", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
        Assert.NotNull(indexer.Getter);
        Assert.Null(indexer.Setter);
    }

    [Fact]
    public void AnIndexerWhoseOnlyAccessorIsInitAlsoHasAnEmptyAccessorList()
    {
        var indexer = Build(Class(), [], Init("block", 50));

        var diagnostics = f.Diagnostics.ToImmutableArray();
        Assert.Equal(["MIST0312", "MIST0311"], diagnostics.Select(diagnostic => diagnostic.Number.Value));
        Assert.All(diagnostics, diagnostic => Assert.Equal(31, diagnostic.Position().Start.Column));
        Assert.Null(indexer.Getter);
        Assert.Null(indexer.Setter);
    }

    [Theory]
    [InlineData("block-get")]
    [InlineData("expression-get")]
    [InlineData("block-set")]
    [InlineData("expression-set")]
    public void ABodyOnAnIntrinsicIndexersAccessorIsRefusedAtTheAccessor(string accessor)
    {
        AccessorDeclarationNode written = accessor switch
        {
            "block-get" => BlockGet(40),
            "expression-get" => WithExpression(BindingFragments.At<ExpressionGetAccessorAccessorDeclarationNode>(40)),
            "block-set" => WithBody(BindingFragments.At<BlockSetAccessorAccessorDeclarationNode>(40)),
            _ => WithExpression(BindingFragments.At<ExpressionSetAccessorAccessorDeclarationNode>(40)),
        };

        _ = Build(Class(intrinsic: true), [new PublicModifierModifierNode(), new IntrinsicModifierModifierNode()], written);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0326", diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AnIntrinsicIndexerOutsideAnIntrinsicTypeIsRefused()
    {
        _ = Build(Class(), [new PublicModifierModifierNode(), new IntrinsicModifierModifierNode()], BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(40));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0328", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void ANonPublicIntrinsicIndexerIsRefused()
    {
        _ = Build(Class(intrinsic: true), [new PrivateModifierModifierNode(), new IntrinsicModifierModifierNode()], BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(40));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0329", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    private Indexer Build(MemberType host, ModifierNode[] modifiers, params AccessorDeclarationNode[] accessors)
    {
        var declaration = BindingFragments.At<IndexerDeclarationNode>();
        declaration.Modifiers = [.. modifiers];
        declaration.Type = BindingFragments.TypeName("Int32");
        declaration.Parameters = new ParameterListNode { Parameter = new ParameterNode { Type = BindingFragments.TypeName("Int32"), Name = BindingFragments.Token("slot") }, Parameters = [] };
        declaration.Body = new BlockIndexerBodyIndexerBodyNode { Accessors = [.. accessors] };

        var resolver = f.Builder.Resolver;
        new IndexerBuilder(f.Builder, resolver, new AccessorBuilder(f.Builder),
            new ParameterBuilder(resolver, f.Builder.Attributes, f.Diagnostics), new ConstraintResolver(f.Builder, resolver)).Resolve(declaration, host);
        return Assert.Single(host.IndexersList);
    }

    private ClassType Class(bool intrinsic = false)
    {
        return new ClassType("Holder", "Probe", f.Types, null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = intrinsic },
        };
    }

    private static BlockGetAccessorAccessorDeclarationNode BlockGet(int offset)
    {
        return WithBody(BindingFragments.At<BlockGetAccessorAccessorDeclarationNode>(offset));
    }

    private static AccessorDeclarationNode Init(string form, int offset)
    {
        return form switch
        {
            "auto" => BindingFragments.At<AutoInitAccessorAccessorDeclarationNode>(offset),
            "block" => WithBody(BindingFragments.At<BlockInitAccessorAccessorDeclarationNode>(offset)),
            _ => WithExpression(BindingFragments.At<ExpressionInitAccessorAccessorDeclarationNode>(offset)),
        };
    }

    private static T WithBody<T>(T accessor) where T : AccessorDeclarationNode
    {
        switch(accessor)
        {
            case BlockGetAccessorAccessorDeclarationNode get:
                get.Body = new BlockNode();
                break;
            case BlockSetAccessorAccessorDeclarationNode set:
                set.Body = new BlockNode();
                break;
            case BlockInitAccessorAccessorDeclarationNode init:
                init.Body = new BlockNode();
                break;
        }

        return accessor;
    }

    private static T WithExpression<T>(T accessor) where T : AccessorDeclarationNode
    {
        var expression = BindingFragments.Expression(BindingFragments.Identifier("slot"));
        switch(accessor)
        {
            case ExpressionGetAccessorAccessorDeclarationNode get:
                get.Expression = expression;
                break;
            case ExpressionSetAccessorAccessorDeclarationNode set:
                set.Expression = expression;
                break;
            case ExpressionInitAccessorAccessorDeclarationNode init:
                init.Expression = expression;
                break;
        }

        return accessor;
    }
}