using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

// Captured offsets never read their symbols again; identity distinguishes non-exact bound descriptors at joins.
internal sealed record RangeBound(Admitted Values, object? Symbol, bool FromEnd)
{
    public static RangeBound? Of(Expression expression, Func<Expression, Admitted?>? captured = null, Func<Expression, RangeBound?>? indexes = null, Func<Expression, RangeBound?>? lengths = null)
    {
        if(expression is CloneExpression clone)
        {
            return Of(clone.Source, captured, indexes, lengths);
        }

        if(expression is ImplicitConversionExpression { Operator: null, AmbiguousOperators.Count: 0 } conversion && IsIndex(conversion.Type))
        {
            return Of(conversion.Operand, captured, indexes, lengths);
        }

        if(expression is UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd, Operator.Modifiers.IsIntrinsic: false })
        {
            return null;
        }

        if(IsIndex(expression.Type) && expression is not UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd })
        {
            return SymbolOf(expression) is not null ? indexes?.Invoke(expression) : null;
        }

        var fromEnd = expression is UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd };
        var offset = fromEnd ? ((UnaryExpression)expression).Operand : expression;

        if(!IsOffset(offset.Type))
        {
            return null;
        }

        if(Requirement.Constant(offset) is { } constant)
        {
            return new RangeBound(Admitted.Exactly(constant), null, fromEnd);
        }

        if(lengths?.Invoke(offset) is { } length)
        {
            return length with { FromEnd = fromEnd };
        }

        if(SymbolOf(offset) is not { } symbol)
        {
            return null;
        }

        var values = captured?.Invoke(offset) ?? Requirement.ValuesOf(offset.Type) ?? Admitted.Everything;

        return new RangeBound(values, Lengths.Exact(values) is null ? symbol : null, fromEnd);
    }

    public static bool IsIndex(IMistType type)
    {
        return DomainOf(type)?.FullyQualifiedName == "Standard.Index";
    }

    private static object? SymbolOf(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression { Local.IsRef: false } local => local.Local,
            FieldReferenceExpression { Field.Modifiers.IsRef: false } field => new FieldBoundSymbol(field.Field, null),
            PropertyReferenceExpression { Property.Modifiers.IsRef: false } property => new PropertyBoundSymbol(property.Property, null),
            MemberAccessExpression { IsConditional: false, Member: FieldResult { Field.Modifiers.IsRef: false } field } member when ReceiverOf(member.Receiver, field.Field.Modifiers.IsStatic) is { } receiver => new FieldBoundSymbol(field.Field, receiver.Local),
            MemberAccessExpression { IsConditional: false, Member: PropertyResult { Property.Modifiers.IsRef: false } property } member when ReceiverOf(member.Receiver, property.Property.Modifiers.IsStatic) is { } receiver => new PropertyBoundSymbol(property.Property, receiver.Local),
            _ => null,
        };
    }

    private static BoundReceiver? ReceiverOf(Expression expression, bool isStatic)
    {
        return expression switch
        {
            ThisExpression when !isStatic => new BoundReceiver(null),
            LocalReferenceExpression { Local.IsRef: false } local when !isStatic => new BoundReceiver(local.Local),
            TypeReferenceExpression when isStatic => new BoundReceiver(null),
            _ => null,
        };
    }

    private static bool IsOffset(IMistType type)
    {
        return DomainOf(type) is { } domain && Requirement.IsIntegral(domain);
    }

    private static IMistType? DomainOf(IMistType type)
    {
        var visited = new HashSet<IMistType>(ReferenceEqualityComparer.Instance);

        while(visited.Add(type))
        {
            var domain = type switch
            {
                TypeAlias { AliasedType: { } aliased } => aliased,
                RefinedType refined => refined.BaseType,
                LiftedType lifted => lifted.Type,
                UnknownValueType unknown => unknown.Domain,
                TypeParameter { IsDependent: true, Domain: { } dependent } => dependent,
                _ => null,
            };

            if(domain is null)
            {
                return type;
            }

            type = domain;
        }

        return null;
    }

    private sealed record FieldBoundSymbol(Field Field, LocalVariable? Receiver);

    private sealed record PropertyBoundSymbol(Property Property, LocalVariable? Receiver);

    private sealed record BoundReceiver(LocalVariable? Local);
}

internal sealed record StoredRange(RangeBound Start, RangeBound End)
{
    public static StoredRange? Of(RangeExpression range, Func<Expression, Admitted?>? captured = null, Func<Expression, RangeBound?>? indexes = null, Func<Expression, RangeBound?>? lengths = null)
    {
        var start = range.Start is null ? new RangeBound(Admitted.Exactly(0), null, false) : RangeBound.Of(range.Start, captured, indexes, lengths);
        var end = range.End is null ? new RangeBound(Admitted.Exactly(0), null, true) : RangeBound.Of(range.End, captured, indexes, lengths);

        return start is not null && end is not null ? new StoredRange(start, end) : null;
    }
}