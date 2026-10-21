using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A call yielding nothing, written where its value is taken. C# has no value for such a call, so it runs as a
/// statement ahead of the one holding it and the unit instance stands in its place. Every operand C# would have
/// evaluated before the call is stored in a temporary ahead of it, so evaluation order survives: in
/// <c>g(k(), f(), h())</c> k still runs before f and h after. A local, a literal, <c>this</c> and a lambda
/// need no temporary, no call being able to change what they are. A value-type receiver or a by-reference
/// argument is read by C# as a location when the call is made, so what is stored ahead of it is what the
/// location is made of -- the object holding the field, the index -- and never the value in it. A value-type
/// receiver that is no location is a copy already, and is stored whole unless a call returned it by reference.
/// <para>
/// A call under an operand that may not run -- the right side of a logical operator, a coalesce, a
/// conditional access's arguments, a <c>??=</c> value, a switch arm -- cannot move ahead of its statement.
/// The conditional holding it is lowered instead (<see cref="ConditionalLowering"/>): it runs as statements
/// into a temporary that takes its place, and the operand is planned afresh inside the branch that runs it.
/// </para>
/// </summary>
internal static class UnitHoisting
{
    [ThreadStatic]
    private static Dictionary<Expression, string>? substitutions;

    [ThreadStatic]
    private static Block? body;

    public static string? Substitution(Expression expression)
    {
        return substitutions?.GetValueOrDefault(expression);
    }

    public static Body? Enter(Block block)
    {
        if(body is not null)
        {
            return null;
        }

        body = block;
        return new Body();
    }

    public static Hoisting Plan(Expression? root, bool discarded = false, bool branchTaken = false)
    {
        var planner = new Planner(root, branchTaken);

        if(root is not null)
        {
            _ = planner.Visit(root, isValue: !discarded);
        }

        return new Hoisting(planner.Steps, planner.Unplaced);
    }

    public static Hoisting Plan(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        var planner = new Planner(null, branchTaken: false);
        _ = planner.VisitAll([.. Arguments(arguments, parameters, conditional: false)]);
        return new Hoisting(planner.Steps, planner.Unplaced);
    }

    private static bool YieldsNothing(InvocationExpression call)
    {
        return TaskResult.Dropped(call) is { } task
            ? TaskResult.Of(task is InvocationExpression started ? started.Method?.ReturnType ?? task.Type : task.Type) is { } awaited && OptionalUnion.IsVoid(awaited)
            : OptionalUnion.IsVoid(call.Method?.ReturnType ?? call.Type);
    }

    private static bool TakesUnit(Expression expression)
    {
        return UnitCalls(expression).Any();
    }

    private static IEnumerable<InvocationExpression> UnitCalls(Expression expression)
    {
        if(Substitution(expression) is not null)
        {
            yield break;
        }

        if(expression is InvocationExpression call && YieldsNothing(call))
        {
            yield return call;
        }

        foreach(var operand in Operands(expression))
        {
            foreach(var nested in UnitCalls(operand.Expression))
            {
                yield return nested;
            }
        }
    }

    private static List<Operand> Operands(Expression expression)
    {
        return expression switch
        {
            InvocationExpression invocation => [.. Callee(invocation.Receiver), .. Arguments(invocation.Arguments, invocation.Parameters, invocation.Receiver is MemberAccessExpression { IsConditional: true })],
            NewExpression creation => [.. Arguments(creation.Arguments, creation.Constructor?.ParametersList, conditional: false), .. creation.Initializer.Select(entry => new Operand(entry.Value))],
            MemberAccessExpression access => [Receiver(access.Receiver)],
            IndexExpression index => [Receiver(index.Receiver), new Operand(index.Index)],
            ExtensionPropertyAccessExpression extension => [Receiver(extension.Receiver)],
            FunctionReferenceExpression { Receiver: { } bound } => [Receiver(bound)],
            BinaryExpression { OperatorKind: BinaryOperatorKind.LogicalAnd or BinaryOperatorKind.LogicalOr } chain => Links(chain),
            BinaryExpression binary => [new Operand(binary.Left), new Operand(binary.Right)],
            CoalesceExpression coalesce => [new Operand(coalesce.Left), new Operand(coalesce.Right, Conditional: true)],
            AssignmentExpression assignment => [.. Target(assignment.Target), new Operand(assignment.Value, Conditional: assignment.IsCoalescing)],
            UnaryExpression unary => [new Operand(unary.Operand)],
            CastExpression cast => [new Operand(cast.Operand)],
            ImplicitConversionExpression conversion => [new Operand(conversion.Operand)],
            AsExpression asExpression => [new Operand(asExpression.Operand)],
            IsPatternExpression isPattern => [new Operand(isPattern.Scrutinee)],
            CloneExpression clone => [new Operand(clone.Source)],
            SpreadExpression spread => [new Operand(spread.Operand, Reading: Reading.Fixed)],
            AsyncExpression { IsLift: false } suppression => [new Operand(suppression.Operand)],
            InterpolatedStringExpression interpolated => [.. interpolated.Parts.OfType<InterpolatedStringHole>().Select(hole => new Operand(hole.Expression))],
            TupleLiteralExpression tuple => [.. tuple.Elements.Select(element => new Operand(element))],
            ArrayLiteralExpression array => [.. array.Elements.Select(element => new Operand(element))],
            AnonymousObjectExpression anonymous => [.. anonymous.Values.Select(value => new Operand(value))],
            ArrayCreationExpression creation => [new Operand(creation.Size)],
            StackallocExpression allocation => [new Operand(allocation.Size)],
            SwitchExpression switchExpression => [new Operand(switchExpression.Scrutinee), .. switchExpression.Arms.SelectMany(Arm)],
            RangeExpression range => [.. new[] { range.Start, range.End }.OfType<Expression>().Select(bound => new Operand(bound))],
            _ => [],
        };
    }

    private static List<Operand> Links(BinaryExpression chain)
    {
        return [.. GuardChain.Links(chain, chain.OperatorKind).Select((link, i) => new Operand(link, Conditional: i > 0))];
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
            parameters is null || i >= parameters.Count || ArgumentKeywords.For(parameters[i]).Length == 0 ? Reading.Value : Reading.Reference));
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
        return new Operand(receiver, Reading: TypeNameEmitter.IsValueKind(TypeNameEmitter.Closed(receiver.Type)) ? Reading.Receiver : Reading.Value);
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

    private static bool IsLocation(Expression expression)
    {
        return expression is MemberAccessExpression { Member: FieldResult } or IndexExpression;
    }

    private static bool IsCopy(Expression expression)
    {
        return expression is not (FieldReferenceExpression or InvocationExpression { Signature.Modifiers.IsRef: true });
    }

    private sealed class Planner(Expression? root, bool branchTaken)
    {
        private readonly HashSet<Expression> spilled = new(ReferenceEqualityComparer.Instance);

        public List<Step> Steps { get; } = [];

        public List<Unplaced> Unplaced { get; } = [];

        public bool Visit(Expression expression, bool isValue)
        {
            if(Substitution(expression) is not null)
            {
                return false;
            }

            var operands = branchTaken && ReferenceEquals(expression, root)
                ? [.. Operands(expression).Select(operand => operand with { Conditional = false })]
                : Operands(expression)
                ;

            var hoisted = VisitAll(operands);

            if(operands.Exists(operand => operand.Conditional && TakesUnit(operand.Expression)))
            {
                return Lower(expression, operands, isValue) || hoisted;
            }

            if(!isValue || expression is not InvocationExpression call || !YieldsNothing(call))
            {
                return hoisted;
            }

            Steps.Add(new Step(expression, StepKind.Hoist));
            return true;
        }

        public bool VisitAll(List<Operand> operands)
        {
            var hoisted = false;
            var starts = new int[operands.Count + 1];
            var last = -1;

            for(var i = 0; i < operands.Count; i++)
            {
                starts[i] = Steps.Count;

                if(!operands[i].Conditional && Visit(operands[i].Expression, isValue: true))
                {
                    last = i;
                    hoisted = true;
                }
            }

            starts[operands.Count] = Steps.Count;

            for(var i = last - 1; i >= 0; i--)
            {
                Steps.InsertRange(starts[i + 1], Spills(operands[i]));
            }

            return hoisted;
        }

        private bool Lower(Expression expression, List<Operand> operands, bool isValue)
        {
            if(Refusal(expression) is { } reason)
            {
                foreach(var operand in operands.Where(operand => operand.Conditional))
                {
                    Unplaced.AddRange(UnitCalls(operand.Expression).Select(call => new Unplaced(call, reason)));
                }

                return false;
            }

            if(expression is InvocationExpression or AssignmentExpression)
            {
                foreach(var operand in operands.Where(operand => !operand.Conditional))
                {
                    Steps.AddRange(Spills(operand));
                }
            }

            Steps.Add(new Step(expression, StepKind.Lower, isValue));
            return true;
        }

        private string? Refusal(Expression expression)
        {
            if(expression is SwitchExpression switchExpression
                && switchExpression.Arms.Any(arm => arm.Guard is { } guard && TakesUnit(guard)))
            {
                return "in a switch arm's guard, which C# evaluates in a case label that holds no statement";
            }

            return OutlivesLowering(expression)
                ? "in a conditional binding a pattern variable that is read after it, where C# would not see the variable as assigned"
                : null
                ;
        }

        private bool OutlivesLowering(Expression expression)
        {
            var bound = BodyWalker.DescendantsAndSelf(expression)
                .OfType<Pattern>()
                .Select(pattern => pattern.Binding)
                .OfType<LocalVariable>()
                .ToHashSet();

            return bound.Count > 0 && References(body ?? (object?)root ?? expression, bound) > References(expression, bound);
        }

        private static int References(object scope, HashSet<LocalVariable> locals)
        {
            return BodyWalker.DescendantsAndSelf(scope)
                .OfType<LocalReferenceExpression>()
                .Count(reference => locals.Contains(reference.Local));
        }

        private IEnumerable<Step> Spills(Operand operand)
        {
            var expression = operand.Expression;

            if(operand.Conditional || IsStable(expression) || Substitution(expression) is not null || spilled.Contains(expression))
            {
                return [];
            }

            if(operand.Reading is Reading.Receiver or Reading.Reference && IsLocation(expression))
            {
                return [.. Operands(expression).SelectMany(Spills)];
            }

            if(operand.Reading is Reading.Value || (operand.Reading is Reading.Receiver && IsCopy(expression)))
            {
                _ = spilled.Add(expression);
                return [new Step(expression, StepKind.Spill)];
            }

            return [];
        }
    }

    internal enum StepKind
    {
        Spill,
        Hoist,
        Lower,
    }

    private enum Reading
    {
        Value,
        Receiver,
        Reference,
        Fixed,
    }

    internal sealed record Step(Expression Expression, StepKind Kind, bool Valued = true);

    internal sealed record Unplaced(InvocationExpression Call, string Reason);

    private sealed record Operand(Expression Expression, bool Conditional = false, Reading Reading = Reading.Value);

    internal sealed class Body : IDisposable
    {
        public void Dispose()
        {
            body = null;
        }
    }

    internal sealed class Hoisting(IReadOnlyList<Step> steps, IReadOnlyList<Unplaced> unplaced) : IDisposable
    {
        private readonly List<Expression> bound = [];

        public IReadOnlyList<Step> Steps { get; } = steps;

        public IReadOnlyList<Unplaced> Unplaced { get; } = unplaced;

        public bool IsEmpty => Steps.Count == 0 && Unplaced.Count == 0;

        public bool Consumes(Expression root)
        {
            return Steps is [.., { Kind: StepKind.Lower, Valued: false } last] && ReferenceEquals(last.Expression, root);
        }

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