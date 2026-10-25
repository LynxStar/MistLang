using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class TargetTypedNewInitializerOnlyBinderTests
{
    [Fact]
    public void ATypedLocal_BindsLikeAnEmptyArgumentListWithTheSameBlock()
    {
        var f = new BindingFragments();
        var thing = HandBuiltModel.Class("Thing");
        var constructor = Constructor(thing);
        thing.ConstructorsList.Add(constructor);
        f.Register(thing);
        var initializerOnly = InitializerOnly();
        var withParentheses = BindingFragments.At<TargetTypedNewExpressionPrimaryNode>(40);
        withParentheses.Initializer = new PropertyInitializerBlockNode();

        var bound = Assert.IsType<NewExpression>(BindLocal(f, "made", initializerOnly).Initializer);
        var control = Assert.IsType<NewExpression>(BindLocal(f, "control", withParentheses).Initializer);

        Assert.Same(initializerOnly, bound.Node);
        Assert.Same(thing, bound.Type);
        Assert.Same(constructor, bound.Constructor);
        Assert.Empty(bound.Arguments);
        Assert.Empty(bound.Initializer);
        Assert.Same(control.Type, bound.Type);
        Assert.Same(control.Constructor, bound.Constructor);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnArgument_IsTrialBoundUnderEachCandidateAndSettlesOnTheOneItConstructs()
    {
        var f = new BindingFragments();
        f.Register(f.Built.Type("Array<,>"));
        var owner = HandBuiltModel.Class("Api");
        var thing = HandBuiltModel.Class("Thing");
        var other = HandBuiltModel.Class("Other");
        var intType = f.Built.Type("Int32");
        var chosen = BindingFragments.Member(owner, "Pick", intType, thing);
        _ = BindingFragments.Member(owner, "Pick", intType, other);
        other.ConstructorsList.Add(Constructor(other, intType, intType));
        _ = f.Local("api", owner);
        var call = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>(50);
        call.Name = BindingFragments.Token("Pick");
        call.Arguments = BindingFragments.Arguments(BindingFragments.Expression(InitializerOnly()));

        var bound = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("api"), call));

        Assert.Same(chosen, bound.Method);
        Assert.Same(thing, Assert.IsType<NewExpression>(Assert.Single(bound.Arguments)).Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnInferredLocal_IsRefusedForWantOfATargetAtTheExpression()
    {
        var f = new BindingFragments();
        var initializerOnly = InitializerOnly();

        var declaration = BindLocal(f, "c", initializerOnly, typed: false);

        Assert.True(declaration.Local.IsFailed);
        Assert.Same(initializerOnly, Assert.IsType<ErrorExpression>(declaration.Initializer).Node);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_TARGET_TYPED_NEW_NO_TARGET", diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Location.Start.Column);
    }

    private static TargetTypedNewExpressionNoArgsPrimaryNode InitializerOnly()
    {
        var node = BindingFragments.At<TargetTypedNewExpressionNoArgsPrimaryNode>(40);
        node.Initializer = new PropertyInitializerBlockNode();
        return node;
    }

    private static Constructor Constructor(ClassType owner, params IMistType[] types)
    {
        return new Constructor(owner.Name, owner, types.Select((type, i) => new Parameter($"p{i}", type)).ToList(), DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = owner.Scope,
        };
    }

    private static LocalDeclarationStatement BindLocal(BindingFragments f, string name, PrimaryNode value, bool typed = true)
    {
        var initializer = BindingFragments.Expression(value);
        var declaration = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = typed
                ? new TypedVariableDeclarationVariableDeclarationStatementNode { Name = BindingFragments.Token(name), Type = BindingFragments.TypeName("Thing"), Initializer = initializer }
                : new InferredVariableDeclarationVariableDeclarationStatementNode { Name = BindingFragments.Token(name), Initializer = initializer },
        };
        return Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(declaration, f.Binding, f.Builder));
    }
}