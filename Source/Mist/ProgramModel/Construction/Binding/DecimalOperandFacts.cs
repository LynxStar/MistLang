using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Binding;

internal static class DecimalOperandFacts
{
    public static RealSet For(BindingContext ctx, Scope scope, Expression operand)
    {
        if(operand is ImplicitConversionExpression { Operator.Modifiers.IsIntrinsic: true } conversion
            && RealRequirement.KindOf(conversion.Type) == RealKind.Decimal
            && (Requirement.IsIntegral(conversion.Operand.Type) || RealRequirement.KindOf(conversion.Operand.Type) == RealKind.Decimal))
        {
            return For(ctx, scope, conversion.Operand);
        }

        if(Requirement.IsIntegral(operand.Type))
        {
            if(IntegerOperandValue.Exact(ctx, scope, operand) is { } value)
            {
                return RealSet.Exactly(RealKind.Decimal, new LiteralValue((decimal)value, "Standard.Decimal"))!;
            }

            return RealSet.Everything(RealKind.Decimal);
        }

        var values = RealRequirement.OfType(operand.Type) ?? RealSet.Everything(RealKind.Decimal);
        if(RealRequirement.LiteralOf(operand) is { } literal && RealSet.Exactly(RealKind.Decimal, literal) is { } exact)
        {
            values = values.And(exact);
        }

        if(operand is LocalReferenceExpression local && !OperandFacts.IsMutable(ctx, local.Local))
        {
            for(var current = scope; current is not null; current = current.Parent)
            {
                if(current is BodyScope body && ctx.DecimalFacts.TryGetValue(body, out var facts)
                    && facts.TryGetValue(local.Local, out var held))
                {
                    values = values.And(held);
                }
            }
        }

        if(operand is BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true } } binary
            && DecimalArithmeticProof.Exact(For(ctx, scope, binary.Left)) is { } left
            && DecimalArithmeticProof.Exact(For(ctx, scope, binary.Right)) is { } right
            && DecimalArithmeticProof.Calculate(binary.OperatorKind, left, right) is { } result)
        {
            values = values.And(RealSet.Exactly(RealKind.Decimal, new LiteralValue(result, "Standard.Decimal"))!);
        }

        return values;
    }

    public static void Declared(BindingContext ctx, BodyScope scope, LocalVariable local, Expression? initializer)
    {
        if(initializer is not null && RealRequirement.KindOf(local.Type) == RealKind.Decimal && !OperandFacts.IsMutable(ctx, local))
        {
            Facts(ctx, scope)[local] = For(ctx, scope, initializer);
        }
    }

    public static void Narrow(BindingContext ctx, BodyScope scope, LocalVariable local, ComparisonKind kind, Expression bound, bool holds)
    {
        if(OperandFacts.IsMutable(ctx, local) || RealRequirement.KindOf(local.Type) != RealKind.Decimal
            || DecimalArithmeticProof.Exact(For(ctx, scope, bound)) is not { } constant
            || RealSet.Compare(RealKind.Decimal, kind, new LiteralValue(constant, "Standard.Decimal")) is not { } narrowed)
        {
            return;
        }

        var facts = Facts(ctx, scope);
        var values = holds ? narrowed : narrowed.Not();
        facts[local] = facts.TryGetValue(local, out var existing) ? existing.And(values) : values;
    }

    private static Dictionary<LocalVariable, RealSet> Facts(BindingContext ctx, BodyScope scope)
    {
        if(!ctx.DecimalFacts.TryGetValue(scope, out var facts))
        {
            facts = new Dictionary<LocalVariable, RealSet>(ReferenceEqualityComparer.Instance);
            ctx.DecimalFacts[scope] = facts;
        }

        return facts;
    }
}