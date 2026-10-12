using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class StaticExtensionInvocationBindingTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AStaticCallPreservesTheSelectedDeclarationAndBothKindsOfTypeArguments(
        bool genericBlock, bool genericMethod, bool explicitMethodArguments)
    {
        var fixture = new Fixture(genericBlock, genericMethod);
        var call = Assert.IsType<InvocationExpression>(fixture.Call("Int32", explicitMethodArguments));
        var selected = Assert.IsType<Method>(call.Method);
        var callee = Assert.IsType<MemberAccessExpression>(call.Receiver);

        Assert.Same(fixture.Source, selected.SourceDeclaration ?? selected);
        Assert.Same(fixture.Block, selected.DeclaringBlock);
        Assert.Same(fixture.Integer, call.Type);
        Assert.Same(fixture.Integer, Assert.Single(call.ParameterTypes!));
        Assert.Equal(genericMethod ? new IMistType[] { fixture.Integer } : [], callee.TypeArguments);

        if(genericBlock)
        {
            Assert.Same(fixture.Block, selected.ClosedBlock);
            Assert.Same(fixture.Integer, selected.BlockClosing.Apply(fixture.Block.TypeParametersList[0], fixture.Fragments.Built.Interner));
            Assert.Same(fixture.Receiver, Assert.IsType<ConstructedType>(Assert.IsType<TypeReferenceExpression>(callee.Receiver).ReferencedType).Definition);
        }
        else
        {
            Assert.Null(selected.ClosedBlock);
        }

        if(genericMethod)
        {
            Assert.Same(fixture.Source.TypeParametersList[0], Assert.Single(selected.TypeParametersList));
        }

        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void DistinctReceiverInstantiationsKeepIndependentClosedSelections()
    {
        var fixture = new Fixture(genericBlock: true, genericMethod: true);
        var integer = Assert.IsType<InvocationExpression>(fixture.Call("Int32", explicitMethodArguments: true));
        var text = Assert.IsType<InvocationExpression>(fixture.Call("String", explicitMethodArguments: false));
        var parameter = fixture.Block.TypeParametersList[0];

        Assert.NotSame(integer.Method, text.Method);
        Assert.Same(fixture.Source, integer.Method!.SourceDeclaration);
        Assert.Same(fixture.Source, text.Method!.SourceDeclaration);
        Assert.Same(fixture.Integer, integer.Method.BlockClosing.Apply(parameter, fixture.Fragments.Built.Interner));
        Assert.Same(fixture.Fragments.Built.Type("String"), text.Method.BlockClosing.Apply(parameter, fixture.Fragments.Built.Interner));
        Assert.Same(parameter, fixture.Source.BlockClosing.Apply(parameter, fixture.Fragments.Built.Interner));
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnOwnStaticMemberShadowsTheGenericBlockMember()
    {
        var fixture = new Fixture(genericBlock: true, genericMethod: false);
        var own = new Method("Make", fixture.Integer, [new Parameter("item", fixture.Integer)], fixture.Source.Modifiers)
        {
            DeclaringType = fixture.Receiver,
            Scope = fixture.Receiver.Scope,
        };
        fixture.Receiver.MethodsList.Add(own);

        var call = Assert.IsType<InvocationExpression>(fixture.Call("Int32", explicitMethodArguments: false));

        Assert.Same(own, call.Method);
        Assert.Null(call.Method!.ClosedBlock);
        Assert.Empty(fixture.Fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AStaticExtensionRequiresItsNamespaceToBeVisible()
    {
        var fixture = new Fixture(genericBlock: true, genericMethod: false);
        fixture.Fragments.Types.WithdrawImport(fixture.Block.NamespaceName);

        _ = fixture.Call("Int32", explicitMethodArguments: false);

        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", Assert.Single(fixture.Fragments.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheReceiverIsVisibleOnlyInsideAnInstanceExtensionMember(bool isStatic)
    {
        var fragments = new BindingFragments();
        var extension = new ExtensionScope(fragments.Types);
        var receiver = new LocalVariable("self", HandBuiltModel.Class("Holder"));
        _ = new ExtensionBlock("self", "Extensions", extension, null) { Receiver = receiver };
        var methodScope = new MethodScope(extension) { IsStatic = isStatic };
        var context = new BindingContext(new BodyScope(methodScope), returnType: null);

        var expression = PostfixBinder.BindPostfix(
            new PostfixNode { Target = BindingFragments.Identifier("self"), Suffix = [] }, context, fragments.Builder);

        if(isStatic)
        {
            _ = Assert.IsType<ErrorExpression>(expression);
            Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", Assert.Single(fragments.Diagnostics.ToImmutableArray()).Id.Value);
        }
        else
        {
            Assert.Same(receiver, Assert.IsType<LocalReferenceExpression>(expression).Local);
            Assert.Empty(fragments.Diagnostics.ToImmutableArray());
        }
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();
        public ClassType Integer => Fragments.Built.Type("Int32");
        public ClassType Receiver { get; }
        public ExtensionBlock Block { get; }
        public Method Source { get; }
        private readonly bool genericBlock;

        public Fixture(bool genericBlock, bool genericMethod)
        {
            this.genericBlock = genericBlock;
            Receiver = HandBuiltModel.Class(genericBlock ? "Box<>" : "Holder");
            Fragments.Register(Receiver);
            Block = new ExtensionBlock("self", "Extensions", new Scope(null), new ExtensionBlockDeclarationNode())
            {
                ResolvedReceiverType = Receiver,
            };

            if(genericBlock)
            {
                Receiver.TypeParametersList.Add(new TypeParameter("T", 0, Receiver.Scope, Receiver));
                var blockParameter = new TypeParameter("T", 0, Block.Scope, null);
                Block.TypeParametersList.Add(blockParameter);
                Block.ResolvedReceiverType = new ConstructedType(Receiver, [blockParameter]);
            }

            var scope = new MethodScope(Block.Scope) { IsStatic = true };
            var methodParameter = new TypeParameter("U", 0, scope, null);
            Source = new Method("Make", Integer, [new Parameter("item", genericMethod ? methodParameter : Integer)],
                DeclarationFragments.Public with { IsStatic = true })
            {
                DeclaringBlock = Block,
                Scope = scope,
            };

            if(genericMethod)
            {
                Source.TypeParametersList.Add(methodParameter);
            }

            Block.MethodsList.Add(Source);
            Fragments.Built.Model.GetOrAddNamespace(Block.NamespaceName).Extensions[Block.ResolvedReceiverType] = [Block];
            Fragments.Types.DeclareImport(Block.NamespaceName);
            _ = Fragments.Local("item", Integer);
        }

        public Expression Call(string receiverArgument, bool explicitMethodArguments)
        {
            var arguments = BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier("item")));

            if(!genericBlock)
            {
                return Fragments.Bind(BindingFragments.Identifier("Holder"), new MethodCallSuffixPostfixSuffixNode
                {
                    Name = BindingFragments.Token("Make"),
                    TypeArgs = explicitMethodArguments ? BindingFragments.TypeArguments("Int32") : null,
                    Arguments = arguments,
                });
            }

            if(explicitMethodArguments)
            {
                var call = BindingFragments.At<GenericTypeMethodCallExpressionPrimaryNode>();
                call.Type = BindingFragments.Name("Box");
                call.TypeArgs = BindingFragments.TypeArguments(receiverArgument);
                call.Member = BindingFragments.Token("Make");
                call.MethodTypeArgs = BindingFragments.TypeArguments("Int32");
                call.Arguments = arguments;
                return Fragments.Bind(call);
            }

            var primary = BindingFragments.At<GenericTypeMemberAccessExpressionPrimaryNode>();
            primary.Type = BindingFragments.Name("Box");
            primary.TypeArgs = BindingFragments.TypeArguments(receiverArgument);
            primary.Member = BindingFragments.Token("Make");
            return Fragments.Bind(primary, new InvocationSuffixPostfixSuffixNode { Arguments = arguments });
        }
    }
}