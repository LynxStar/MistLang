using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A call yielding nothing, written where its value is taken. C# has no value for such a call, so it runs as a
/// statement ahead of the one holding it and the unit instance stands in its place. Every operand C# would have
/// evaluated before the call is stored in a temporary ahead of it, so evaluation order survives: in
/// <c>g(k(), f(), h())</c> k still runs before f and h after. A local, a literal, <c>this</c> and a lambda
/// need no temporary, no call being able to change what they are; nor does a value-type receiver or a
/// by-reference argument, which C# reads as a location when the call is made rather than as a value before
/// it. A call under an operand that may not run -- the right side of a logical operator, a coalesce, a
/// conditional access, a switch arm -- cannot move ahead of its statement, nor can one with no statement of
/// its own to move ahead of, and is refused where its value is taken.
/// </summary>
internal static class UnitHoisting
{
    [ThreadStatic]
    private static Dictionary<Expression, string>? substitutions;

    public static string? Substitution(Expression expression)
    {
        return substitutions?.GetValueOrDefault(expression);
    }

    public static Hoisting Plan(Expression? root)
    {
        var steps = new List<Step>();

        if(root is not null)
        {
            _ = Visit(root, isValue: false, conditional: false, steps);
        }

        return new Hoisting(steps);
    }

    public static void Refuse(Expression value, DiagnosticBag diagnostics)
    {
        if(value is InvocationExpression call && YieldsNothing(call) && Substitution(value) is null)
        {
            diagnostics.ReportAtNode(BackendDiagnostics.UnhoistedUnitCall, value.Node, null, call.Method?.Name ?? "The call");
        }
    }

    private static bool Visit(Expression expression, bool isValue, bool conditional, List<Step> steps)
    {
        var hoisted = false;
        var operands = Operands(expression);
        var starts = new int[operands.Count + 1];
        var last = -1;

        for(var i = 0; i < operands.Count; i++)
        {
            starts[i] = steps.Count;

            if(Visit(operands[i].Expression, isValue: true, conditional || operands[i].Conditional, steps))
            {
                last = i;
                hoisted = true;
            }
        }

        starts[operands.Count] = steps.Count;

        for(var i = last - 1; i >= 0; i--)
        {
            if(operands[i].Spillable && !IsStable(operands[i].Expression))
            {
                steps.Insert(starts[i + 1], new Step(operands[i].Expression, IsHoist: false));
            }
        }

        if(!isValue || conditional || expression is not InvocationExpression call || !YieldsNothing(call))
        {
            return hoisted;
        }

        steps.Add(new Step(expression, IsHoist: true));
        return true;
    }

    private static bool YieldsNothing(InvocationExpression call)
    {
        return OptionalUnion.IsVoid(call.Method?.ReturnType ?? call.Type);
    }

    private static List<Operand> Operands(Expression expression)
    {
        return expression switch
        {
            InvocationExpression invocation => [.. Callee(invocation.Receiver), .. Arguments(invocation.Arguments, invocation.Method?.ParametersList, invocation.Receiver is MemberAccessExpression { IsConditional: true })],
            NewExpression creation => [.. Arguments(creation.Arguments, creation.Constructor?.ParametersList, conditional: false), .. creation.Initializer.Select(entry => new Operand(entry.Value))],
            MemberAccessExpression access => [Receiver(access.Receiver)],
            IndexExpression index => [Receiver(index.Receiver), new Operand(index.Index)],
            ExtensionPropertyAccessExpression extension => [Receiver(extension.Receiver)],
            BinaryExpression binary => [new Operand(binary.Left), new Operand(binary.Right, Conditional: binary.OperatorKind is BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.LogicalOr)],
            CoalesceExpression coalesce => [new Operand(coalesce.Left), new Operand(coalesce.Right, Conditional: true)],
            AssignmentExpression assignment => [.. Target(assignment.Target), new Operand(assignment.Value, Conditional: assignment.IsCoalescing)],
            UnaryExpression unary => [new Operand(unary.Operand)],
            CastExpression cast => [new Operand(cast.Operand)],
            AsExpression asExpression => [new Operand(asExpression.Operand)],
            IsPatternExpression isPattern => [new Operand(isPattern.Scrutinee)],
            CloneExpression clone => [new Operand(clone.Source)],
            SpreadExpression spread => [new Operand(spread.Operand, Spillable: false)],
            AsyncExpression { IsLift: false } suppression => [new Operand(suppression.Operand)],
            InterpolatedStringExpression interpolated => [.. interpolated.Parts.OfType<InterpolatedStringHole>().Select(hole => new Operand(hole.Expression))],
            TupleLiteralExpression tuple => [.. tuple.Elements.Select(element => new Operand(element))],
            ArrayLiteralExpression array => [.. array.Elements.Select(element => new Operand(element))],
            SwitchExpression switchExpression => [new Operand(switchExpression.Scrutinee), .. switchExpression.Arms.SelectMany(Arm)],
            RangeExpression range => [.. new[] { range.Start, range.End }.OfType<Expression>().Select(bound => new Operand(bound))],
            _ => [],
        };
    }

    private static IEnumerable<Operand> Callee(Expression callee)
    {
        return callee switch
        {
            MethodReferenceExpression => [],
            MemberAccessExpression access => [Receiver(access.Receiver)],
            _ => [new Operand(callee)],
        };
    }

    private static IEnumerable<Operand> Arguments(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, bool conditional)
    {
        return arguments.Select((argument, i) => new Operand(
            argument,
            conditional,
            Spillable: parameters is null || i >= parameters.Count || ArgumentKeywords.For(parameters[i]).Length == 0));
    }

    private static IEnumerable<Operand> Target(Expression target)
    {
        return target switch
        {
            MemberAccessExpression access => [Receiver(access.Receiver)],
            IndexExpression index => [Receiver(index.Receiver), new Operand(index.Index)],
            _ => [],
        };
    }

    private static IEnumerable<Operand> Arm(SwitchArm arm)
    {
        return new[] { arm.Guard, arm.Result }.OfType<Expression>().Select(part => new Operand(part, Conditional: true));
    }

    private static Operand Receiver(Expression receiver)
    {
        return new Operand(receiver, Spillable: !TypeNameEmitter.IsValueKind(receiver.Type));
    }

    private static bool IsStable(Expression expression)
    {
        return expression is LiteralExpression
            or VoidLiteralExpression
            or LocalReferenceExpression
            or ThisExpression
            or TypeReferenceExpression
            or TypeLiteralExpression
            or LambdaExpression
            or DefaultExpression
            or RegexLiteralExpression
            or FunctionReferenceExpression { Receiver: null };
    }

    internal sealed record Step(Expression Expression, bool IsHoist);

    private sealed record Operand(Expression Expression, bool Conditional = false, bool Spillable = true);

    internal sealed class Hoisting(IReadOnlyList<Step> steps) : IDisposable
    {
        private readonly List<Expression> bound = [];

        public IReadOnlyList<Step> Steps { get; } = steps;

        public void Bind(Step step, string text)
        {
            substitutions ??= [];
            substitutions[step.Expression] = text;
            bound.Add(step.Expression);
        }

        public void Dispose()
        {
            foreach(var expression in bound)
            {
                _ = substitutions?.Remove(expression);
            }
        }
    }
}