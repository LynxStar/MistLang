using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What the binder knows of a local's value where an operand sits, as the interval set
/// (<see cref="Admitted"/>) the refinement engine already reasons in. This is the binding-time
/// counterpart to the Stage 5 flow: proof-sensitive arithmetic needs the operand facts BEFORE the
/// expression is typed, and Stage 5 cannot retype a finished model. It is deliberately lexical, like
/// the optional narrowing on <see cref="BodyScope"/>: a guard records what it proves into the scope it
/// guards, a reassignment records a barrier in the scope it writes, and a read And-combines the facts
/// of the enclosing scopes until it meets a barrier, so a proof established after a write is kept while
/// the stale fact behind the write is dropped. Facts attach to a <see cref="LocalVariable"/> only:
/// a mutable property, an aliased location or a repeated call manufactures none.
/// </summary>
internal static class OperandFacts
{
    public static Admitted FactFor(BindingContext ctx, Scope scope, LocalVariable local)
    {
        var store = ctx.ArithmeticFacts;
        var result = Admitted.Everything;

        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is BodyScope body && store.TryGetValue(body, out var facts) && facts.TryGetValue(local, out var values))
            {
                if(values.IsEverything)
                {
                    return result;
                }

                result = result.And(values);
            }
        }

        return result;
    }

    public static Admitted For(BindingContext ctx, Scope scope, Expression operand)
    {
        var values = Admitted.Everything;

        if(Requirement.Constant(operand) is { } constant)
        {
            values = values.And(Admitted.Exactly(constant));
        }

        if(Requirement.ValuesOf(operand.Type) is { } declared)
        {
            values = values.And(declared);
        }

        if(operand is LocalReferenceExpression local)
        {
            values = values.And(FactFor(ctx, scope, local.Local));
        }

        // A subexpression of constant parts folds to a constant, so `x / (1024 * 4)` and the proven-safe
        // divisions nested in another prove their own safety too.
        if(operand is BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } arithmetic
            && Exact(For(ctx, scope, arithmetic.Left)) is { } leftValue
            && Exact(For(ctx, scope, arithmetic.Right)) is { } rightValue
            && Fold(arithmetic.OperatorKind, leftValue, rightValue) is { } folded)
        {
            values = values.And(Admitted.Exactly(folded));
        }

        return values;
    }

    private static long? Exact(Admitted values)
    {
        return values.Low is { } low && values.High == low ? low : null;
    }

    private static long? Fold(BinaryOperatorKind op, long left, long right)
    {
        var folded = op switch
        {
            BinaryOperatorKind.Add => left + right,
            BinaryOperatorKind.Subtract => left - right,
            BinaryOperatorKind.Multiply => left * right,
            BinaryOperatorKind.Divide when right != 0 && !(left == int.MinValue && right == -1) => left / right,
            BinaryOperatorKind.Modulo when right != 0 => left % right,
            _ => (long?)null,
        };

        // Fold only where the Int32 result does not wrap, so the proven value is the one the program sees.
        return folded is { } value && value >= int.MinValue && value <= int.MaxValue ? value : null;
    }

    public static void Record(BindingContext ctx, BodyScope scope, LocalVariable local, Admitted values)
    {
        var facts = Facts(ctx, scope);
        facts[local] = facts.TryGetValue(local, out var existing) && !existing.IsEverything ? existing.And(values) : values;
    }

    public static void Reassigned(BindingContext ctx, BodyScope scope, LocalVariable local, Expression value)
    {
        Facts(ctx, scope)[local] = Requirement.Constant(value) is { } constant ? Admitted.Exactly(constant) : Admitted.Everything;
    }

    public static void Declared(BindingContext ctx, BodyScope scope, LocalVariable local, Expression? initializer)
    {
        if(initializer is not null && Requirement.Constant(initializer) is { } constant)
        {
            Facts(ctx, scope)[local] = Admitted.Exactly(constant);
        }
    }

    public static void Learn(BindingContext ctx, BodyScope scope, Expression condition, bool holds)
    {
        switch(condition)
        {
            case BinaryExpression { OperatorKind: BinaryOperatorKind.LogicalAnd, Left: var left, Right: var right } when holds:
                Learn(ctx, scope, left, true);
                Learn(ctx, scope, right, true);
                return;

            case BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } comparison
                when Compare(comparison.OperatorKind) is { } kind:
                var effective = comparison.OperatorKind == BinaryOperatorKind.NotEqual ? !holds : holds;

                if(comparison.Left is LocalReferenceExpression subject && Requirement.Constant(comparison.Right) is { } bound)
                {
                    Narrow(ctx, scope, subject.Local, kind, bound, effective);
                }
                else if(comparison.Right is LocalReferenceExpression mirrored && Requirement.Constant(comparison.Left) is { } limit)
                {
                    Narrow(ctx, scope, mirrored.Local, Mirrored(kind), limit, effective);
                }

                return;

            default:
                return;
        }
    }

    private static void Narrow(BindingContext ctx, BodyScope scope, LocalVariable local, ComparisonKind kind, long constant, bool holds)
    {
        var interval = Requirement.Interval(kind, constant);
        Record(ctx, scope, local, holds ? interval : interval.Not());
    }

    private static Dictionary<LocalVariable, Admitted> Facts(BindingContext ctx, BodyScope scope)
    {
        if(!ctx.ArithmeticFacts.TryGetValue(scope, out var facts))
        {
            facts = new Dictionary<LocalVariable, Admitted>(ReferenceEqualityComparer.Instance);
            ctx.ArithmeticFacts[scope] = facts;
        }

        return facts;
    }

    private static ComparisonKind? Compare(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => ComparisonKind.Equal,
            BinaryOperatorKind.LessThan => ComparisonKind.LessThan,
            BinaryOperatorKind.GreaterThan => ComparisonKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => ComparisonKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            _ => null,
        };
    }

    private static ComparisonKind Mirrored(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThan => ComparisonKind.LessThan,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
            _ => kind,
        };
    }
}