using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltRefinements;

namespace Mist.Tests.ProgramAnalysis;

internal sealed class RangeFlowBody
{
    public HandBuiltRefinements Values { get; } = new();
    public LocalReferenceExpression Numbers { get; }
    public LocalReferenceExpression Window { get; }
    public LocalReferenceExpression Flag { get; }
    public LocalReferenceExpression Slice { get; }

    public RangeFlowBody()
    {
        Numbers = Local(Values.Sequence(), "numbers");
        Window = Local(Values.Range, "window");
        Flag = Local(Values.Bool, "flag");
        Slice = Local(Values.Sequence(), "slice");
    }

    public Expression Number(long value)
    {
        return value < 0
            ? new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Values.Literal(-value), Values.Int)
            : Values.Literal(value);
    }

    public Expression Back(long offset)
    {
        return Values.Back(Number(offset));
    }

    public RangeExpression To(Expression end)
    {
        return Values.Slice(Number(0), end);
    }

    public RangeExpression Bounds(long? start, bool fromEndStart, long? end, bool fromEndEnd)
    {
        Expression? bound(long? offset, bool fromEnd)
        {
            return offset is null ? null : fromEnd ? Back(offset.Value) : Number(offset.Value);
        }

        return Values.Slice(bound(start, fromEndStart), bound(end, fromEndEnd));
    }

    public IndexExpression SliceOf(Expression range, Expression? receiver = null)
    {
        receiver ??= Numbers;
        return new IndexExpression(HandBuiltModel.Node(), receiver, range, receiver.Type);
    }

    public Statement ReadSlice(Expression range, long last, Expression? receiver = null)
    {
        return HandBuiltModel.Block(Give(Slice, SliceOf(range, receiver)), Evaluate(Values.At(Slice, Number(last))));
    }

    public Statement ReadWindow(long last)
    {
        return ReadSlice(Window, last);
    }

    public static Statement Set(Expression target, Expression value)
    {
        return Evaluate(Assign(target, value));
    }

    public Expression InInterval(Expression value, long low, long high)
    {
        return Values.And(Values.Binary(value, BinaryOperatorKind.GreaterThanOrEqual, Number(low)),
            Values.Binary(value, BinaryOperatorKind.LessThanOrEqual, Number(high)));
    }

    public Expression CallWithReturns(string name, long first, long second)
    {
        var parameter = new Parameter("flag", Values.Bool);
        var flag = Read(new LocalVariable("flag", Values.Bool) { Parameter = parameter });
        var body = HandBuiltModel.Block(If(flag, new ReturnStatement(Number(first), Values.Int)), new ReturnStatement(Number(second), Values.Int));
        var method = new Method(name, Values.Int, [parameter], new MemberModifiers { IsStatic = true }) { Body = body };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [Flag], Values.Int, method, [Values.Bool]);
    }

    public static Expression IdentityCall(string name, Expression argument)
    {
        var parameter = new Parameter("value", argument.Type);
        var local = Read(new LocalVariable(parameter.Name, parameter.Type) { Parameter = parameter });
        var method = new Method(name, argument.Type, [parameter], new MemberModifiers { IsStatic = true })
        { Body = HandBuiltModel.Block(new ReturnStatement(local, argument.Type)) };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method),
            [argument], method.ReturnType, method, [parameter.Type]);
    }

    public static Expression CallReturning(string name, IMistType returnType, Expression value)
    {
        var method = new Method(name, returnType, [], new MemberModifiers { IsStatic = true })
        { Body = HandBuiltModel.Block(new ReturnStatement(value, returnType)) };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], returnType, method, []);
    }

    public Expression ArrayLiteral(params long[] elements)
    {
        return new ArrayLiteralExpression(HandBuiltModel.Node(), Values.Int, elements.Select(Number).ToArray(), Values.Sequence(length: elements.Length));
    }

    public Statement LambdaWrite(Expression target, Expression replacement)
    {
        var type = new FunctionType([], Values.Built.Type("Void"));
        var change = Local(type, "change");
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [], null, HandBuiltModel.Block(Set(target, replacement)), false, type);
        return HandBuiltModel.Block(Give(change, lambda),
            Evaluate(new InvocationExpression(HandBuiltModel.Node(), change, [], type.ReturnType, null, [])));
    }

    public static Statement RefWrite(Expression target, Expression replacement)
    {
        var alias = Read(new LocalVariable("alias", target.Type) { IsRef = true });
        return HandBuiltModel.Block(Give(alias, target), Set(alias, replacement));
    }

    public Statement PassByRef(Expression target, Expression replacement)
    {
        var parameter = new Parameter("value", target.Type, new ParameterModifier(false, true, default, ByRefDirection.Ref));
        var local = Read(new LocalVariable(parameter.Name, parameter.Type) { Parameter = parameter, IsRef = true });
        var method = new Method("Change", Values.Built.Type("Void"), [parameter], new MemberModifiers { IsStatic = true })
        { Body = HandBuiltModel.Block(Set(local, replacement)) };
        return Evaluate(new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [target], method.ReturnType, method, [target.Type]));
    }

    public Block WithNumbers(params Statement[] statements)
    {
        return HandBuiltModel.Block([Give(Flag, IdentityCall("Choose", Values.Literal(true))), Give(Numbers, ArrayLiteral(10, 20, 30, 40, 50)), .. statements]);
    }

    public void Check(string? diagnostic, params Statement[] statements)
    {
        var body = WithNumbers(statements);
        var findings = Values.Built.Analyze(new RefinementAnalyzer(), body);
        if(diagnostic is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(diagnostic, Assert.Single(findings).Number.Value);
        }
    }
}