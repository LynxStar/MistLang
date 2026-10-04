using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AsyncLiftDecisionTests
{
    [Fact]
    public void AnImmediateTaskDropNeedsAnAwaitInTheScheduledLambda()
    {
        Assert.True(AsyncLifts.NeedsAwait(Drop()));
    }

    [Fact]
    public void AnImmediateArgumentDropNeedsAnAwaitEvenWhenTheCalleeIsSynchronous()
    {
        var drop = Drop();
        var method = new Method("Identity", drop.Type, [new Parameter("value", drop.Type)], new MemberModifiers());
        var invocation = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method),
            [drop], drop.Type, method, [drop.Type]);

        Assert.True(AsyncLifts.NeedsAwait(invocation));
    }

    [Fact]
    public void ASuppressionStillEvaluatesItsArgumentsInTheEnclosingScheduledLambda()
    {
        var drop = Drop();
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
        var method = new Method("Fetch", task, [new Parameter("value", drop.Type)], new MemberModifiers { IsAsync = true });
        var invocation = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method),
            [drop], task, method, [drop.Type]);
        var suppression = new AsyncExpression(HandBuiltModel.Node(), invocation, task, isLift: false);

        Assert.True(AsyncLifts.NeedsAwait(suppression));
    }

    [Theory]
    [InlineData("lambda")]
    [InlineData("block")]
    [InlineData("functor")]
    public void DeferredBodiesDoNotMakeTheirEnclosingLiftAsync(string kind)
    {
        var drop = Drop();
        Expression expression = kind switch
        {
            "lambda" => new LambdaExpression(HandBuiltModel.Node(), [], drop, null, false, new FunctionType([], drop.Type)),
            "block" => new AsyncBlockExpression(HandBuiltModel.Node(), HandBuiltModel.Block(new ReturnStatement(drop, drop.Type)),
                drop.Type, isValueTask: false),
            _ => new AsyncExpression(HandBuiltModel.Node(), drop, drop.Type, isLift: true),
        };

        Assert.False(AsyncLifts.NeedsAwait(expression));
    }

    [Fact]
    public void AnOrdinaryInvocationDoesNotIntroduceAnAsyncLambdaWithoutAnAwait()
    {
        var result = new HandBuiltModel().Type("Int32");
        var method = new Method("Read", result, [], new MemberModifiers());
        var invocation = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method),
            [], result, method, []);

        Assert.False(AsyncLifts.NeedsAwait(invocation));
    }

    private static InvocationExpression Drop()
    {
        var built = new HandBuiltModel();
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
        var method = new Method("Drop", built.Type("Int32"), [], new MemberModifiers { IsIntrinsic = true })
        { DeclaringType = task };
        var receiver = HandBuiltModel.Literal(task);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Drop", method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), access, [], method.ReturnType, method, []);
    }
}