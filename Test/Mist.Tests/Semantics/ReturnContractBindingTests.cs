using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ReturnContractBindingTests
{
    private readonly BindingFragments fragment = new();

    [Fact]
    public void AValuedTopLevelReturnAnswersToTheExitCode()
    {
        var returned = Bound(Method(topLevel: true, Returning(Seven())));

        Assert.True(returned.IsExitCode);
        Assert.Same(fragment.Built.Type("Int32"), returned.TargetType);
    }

    [Fact]
    public void ABareTopLevelReturnIsNoExitCode()
    {
        var returned = Bound(Method(topLevel: true, Returning(null)));

        Assert.False(returned.IsExitCode);
        Assert.Same(fragment.Built.Type("Void"), returned.TargetType);
    }

    [Fact]
    public void AValuedReturnInAVoidMethodAnswersToVoid()
    {
        var returned = Bound(Method(topLevel: false, Returning(Seven())));

        Assert.False(returned.IsExitCode);
        Assert.Same(fragment.Built.Type("Void"), returned.TargetType);
    }

    [Fact]
    public void AConstructorBodyReturnsVoid()
    {
        var owner = HandBuiltModel.Class("Holder");
        var constructor = new Constructor(owner.Name, owner, [], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = fragment.Scope,
            Body = Block(Returning(Seven())),
        };

        Assert.Same(fragment.Built.Type("Void"), Bound(constructor).TargetType);
    }

    [Fact]
    public void ASetterBodyReturnsVoid()
    {
        var integer = fragment.Built.Type("Int32");
        var setter = new PropertyAccessor { Body = Block(Returning(Seven())) };

        BodyBinder.BindAccessors(null, setter, integer, [], false, new Property("Count", integer, DeclarationFragments.Public), fragment.Types, fragment.Builder);

        Assert.Same(fragment.Built.Type("Void"), Assert.IsType<ReturnStatement>(Assert.Single(setter.Body!.Statements)).TargetType);
    }

    [Fact]
    public void AReturnInALambdaAmongTheTopLevelStatementsAnswersToTheLambda()
    {
        var lambda = BindingFragments.At<BlockLambdaLambdaExpressionNode>();
        lambda.Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
        lambda.Body = new BlockNode { Statements = [Returning(Seven())] };
        var context = new BindingContext(fragment.Scope, fragment.Built.Type("Void"))
        {
            ExitCodeType = fragment.Built.Type("Int32"),
        };

        var bound = Assert.IsType<LambdaExpression>(LambdaBinder.Bind(lambda, context, fragment.Builder));

        Assert.False(Assert.IsType<ReturnStatement>(Assert.Single(bound.Body!.Statements)).IsExitCode);
        Assert.Same(fragment.Built.Type("Int32"), context.ExitCodeType);
    }

    private Method Method(bool topLevel, StatementNode statement)
    {
        return new Method("Main", fragment.Built.Type("Void"), [], DeclarationFragments.Public)
        {
            Scope = fragment.Scope,
            Body = Block(statement),
            IsTopLevelProgram = topLevel,
        };
    }

    private ReturnStatement Bound(Method method)
    {
        BodyBinder.BindDelegate(method, fragment.Builder);

        return Assert.IsType<ReturnStatement>(Assert.Single(method.Body!.Statements));
    }

    private static Block Block(StatementNode statement)
    {
        return new Block(new BlockNode { Statements = [statement] });
    }

    private static ReturnStatementStatementNode Returning(ExpressionNode? value)
    {
        return new ReturnStatementStatementNode { ReturnStatement = new ReturnStatementNode { Value = value } };
    }

    private static ExpressionNode Seven()
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token("7");
        return BindingFragments.Expression(literal);
    }
}