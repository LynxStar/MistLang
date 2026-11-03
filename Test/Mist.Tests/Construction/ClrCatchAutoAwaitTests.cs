using Mist.Generated;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ClrCatchAutoAwaitTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AutoAwaitUnwrapsTheAdaptedTaskAndLeavesTheForeignOperationInside(bool asyncContext, bool wantsTask)
    {
        var f = new BindingFragments();
        var shapes = new ResultModel();
        var task = f.Built.Type("Task<>");
        var parameter = new TypeParameter("T", 0, task.Scope, task);
        task.TypeParametersList.Add(parameter);
        var drop = new Method("Drop", parameter, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task };
        task.MethodsList.Add(drop);
        f.Register(task);
        var foreignReturn = f.Built.Interner.GetOrCreateConstructedType(task, [f.Built.Type("Int32")]);
        _ = f.Local("operation", new FunctionType([], foreignReturn));
        var method = ClrCatchBindingTests.Catch(shapes);
        var group = new MethodGroupExpression(Node(), null, "Catch", [method], null);
        f.Binding.IsAsyncContext = asyncContext;
        var expected = f.Built.Interner.GetOrCreateConstructedType(task, [shapes.Result(f.Built.Type("Int32"), shapes.Text)]);
        f.Binding.TargetType = wantsTask ? expected : null;
        var bound = PostfixBinder.BindInvocation(Node(), group,
            BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier("operation"))), null, f.Binding, f.Builder);

        var adapted = asyncContext || wantsTask ? bound : TaskResult.Dropped(bound);
        var call = Assert.IsType<InvocationExpression>(adapted);
        Assert.Same(method, call.Method);
        Assert.Same(foreignReturn, Assert.IsType<FunctionType>(Assert.Single(call.Arguments).Type).ReturnType);
        var result = ResultTypes.Of(TaskResult.Of(call.Type));
        Assert.NotNull(result);
        Assert.Same(f.Built.Type("Int32"), result.Success);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        if(!asyncContext && !wantsTask)
        {
            Assert.Same(drop, Assert.IsType<InvocationExpression>(bound).Method);
            Assert.Same(result.Type, bound.Type);
        }
    }

    [Fact]
    public void TakingTheBoundaryAsAValueCarriesTheAdaptedTaskSignature()
    {
        var f = new BindingFragments();
        var shapes = new ResultModel();
        var task = f.Built.Type("Task<>");
        var parameter = new TypeParameter("T", 0, task.Scope, task);
        task.TypeParametersList.Add(parameter);
        task.MethodsList.Add(new Method("Drop", parameter, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task });
        f.Register(task);

        var foreignReturn = f.Built.Interner.GetOrCreateConstructedType(task, [f.Built.Type("Int32")]);
        var operation = new FunctionType([], foreignReturn);
        var adapted = f.Built.Interner.GetOrCreateConstructedType(task, [shapes.Result(f.Built.Type("Int32"), shapes.Text)]);
        var method = ClrCatchBindingTests.Catch(shapes);
        var group = new MethodGroupExpression(Node(), null, "Catch", [method], null);

        // The value form carries the task-adapted Task<Result<T, E>> the direct call produces, not the
        // declared Result<Task<T>, E> that covers only task creation: the adapted target converts and the
        // unadapted one is refused rather than silently accepted.
        var converted = MethodGroupConversion.Convert(group, new FunctionType([operation], adapted), f.Builder, out _);
        var reference = Assert.IsType<FunctionReferenceExpression>(converted);
        Assert.Same(method, reference.Method);
        var delegateReturn = Assert.IsType<FunctionType>(reference.Type).ReturnType;
        var intercepted = ResultTypes.Of(TaskResult.Of(delegateReturn));
        Assert.NotNull(intercepted);
        Assert.Same(f.Built.Type("Int32"), intercepted.Success);

        var declared = new FunctionType([operation], shapes.Result(foreignReturn, shapes.Text));
        Assert.Null(MethodGroupConversion.Convert(group, declared, f.Builder, out _));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnInnerTaskCallStillAwaitsInsideALargerBoundaryLambdaResult()
    {
        var f = TaskReadingModel(out _, out var box);
        var lambda = Lambda(
            new InvocationSuffixPostfixSuffixNode(),
            new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token("Value") });
        var result = new LambdaTrials(f.Binding, f.Builder, retainTaskResult: true).ResultType(lambda, []);

        Assert.Same(box.FieldsList[0].Type, result);
    }

    [Fact]
    public void OnlyTheBoundaryLambdasOwnResultIsKeptAsItsTask()
    {
        var f = TaskReadingModel(out var taskOfBox, out var box);
        var lambda = Lambda(new InvocationSuffixPostfixSuffixNode());

        Assert.Same(taskOfBox, new LambdaTrials(f.Binding, f.Builder, retainTaskResult: true).ResultType(lambda, []));
        Assert.Same(box, new LambdaTrials(f.Binding, f.Builder, retainTaskResult: false).ResultType(lambda, []));
    }

    private static BindingFragments TaskReadingModel(out ConstructedType taskOfBox, out ClassType box)
    {
        var f = new BindingFragments();
        var task = f.Built.Type("Task<>");
        var parameter = new TypeParameter("T", 0, task.Scope, task);
        task.TypeParametersList.Add(parameter);
        task.MethodsList.Add(new Method("Drop", parameter, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task });
        f.Register(task);

        box = Class("Box");
        box.FieldsList.Add(new Field("Value", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = box });
        taskOfBox = f.Built.Interner.GetOrCreateConstructedType(task, [box]);
        _ = f.Local("read", new FunctionType([], taskOfBox));
        return f;
    }

    private static UnboundLambdaExpression Lambda(params PostfixSuffixNode[] suffixes)
    {
        var node = BindingFragments.At<ExpressionLambdaLambdaExpressionNode>();
        node.Modifiers = [];
        node.Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
        node.Body = BindingFragments.Expression(BindingFragments.Identifier("read"), suffixes);
        return new UnboundLambdaExpression(node);
    }
}