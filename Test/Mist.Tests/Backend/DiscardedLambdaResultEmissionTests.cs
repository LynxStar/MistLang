using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DiscardedLambdaResultEmissionTests
{
    private readonly HandBuiltModel model = new();

    private IMistType Int => model.Type("Int32");

    private IMistType Void => model.Type("Void");

    [Fact]
    public void AValueBodyAtAVoidResult_IsDiscardedInsideABlock()
    {
        var x = new LocalVariable("x", Int);
        var sum = new BinaryExpression(HandBuiltModel.Node(), Read(x), BinaryOperatorKind.Add, HandBuiltModel.Literal(Int, 1L), Int);

        var emitted = Emit(new LambdaExpression(HandBuiltModel.Node(), [x], sum, null, false, new FunctionType([Int], Void)));

        Assert.Contains("_ = x + 1;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("=> x + 1", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueBodyWhoseOperandsRunAhead_IsStillDiscarded()
    {
        var owner = HandBuiltModel.Class("Log");
        var f = new Method("F", Void, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        var show = new Method("Show", Int, [new Parameter("unit", Void)], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        var shown = Call(show, Call(f));
        var sum = new BinaryExpression(HandBuiltModel.Node(), shown, BinaryOperatorKind.Add, HandBuiltModel.Literal(Int, 1L), Int);
        var x = new LocalVariable("x", Int);

        var emitted = Emit(new LambdaExpression(HandBuiltModel.Node(), [x], sum, null, false, new FunctionType([Int], Void)));

        Assert.Contains("F();", emitted, StringComparison.Ordinal);
        Assert.Contains("_ = Show(Standard.Void.Instance) + 1;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssignmentOrCallBody_StaysAnExpressionBody()
    {
        var counter = new LocalVariable("counter", Int);
        var x = new LocalVariable("x", Int);
        var assignment = new AssignmentExpression(HandBuiltModel.Node(), Read(counter), Read(x), Int);
        var owner = HandBuiltModel.Class("Log");
        var f = new Method("F", Int, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };

        Assert.Equal("(int x) => counter = x", Emit(new LambdaExpression(HandBuiltModel.Node(), [x], assignment, null, false, new FunctionType([Int], Void))));
        Assert.Equal("(int x) => F()", Emit(new LambdaExpression(HandBuiltModel.Node(), [x], Call(f), null, false, new FunctionType([Int], Void))));
    }

    private static LocalReferenceExpression Read(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    private static InvocationExpression Call(Method method, params Expression[] arguments)
    {
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), arguments,
            method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag()).ReplaceLineEndings("\n");
    }
}