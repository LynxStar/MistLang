using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a comparison pattern can be asked of a value of a type: a relational pattern orders only a
/// primitive numeric, a char, or a refinement or enum over one -- a user type's declared comparison
/// operators do not count, since a relational pattern is not a call to one, and neither does a type
/// parameter under any constraint -- and a constant pattern compares only a constant some value of the
/// type could equal. A pattern in a body and a refinement predicate on a declaration ask these same
/// questions, so the two cannot disagree about a type; an indeterminate input admits any test, since
/// that is what a test of it is for.
/// </summary>
internal static class Comparability
{
    private static readonly string[] ComparableNames =
    [
        WellKnownTypes.Byte.FullyQualifiedName, WellKnownTypes.SByte.FullyQualifiedName,
        WellKnownTypes.Int16.FullyQualifiedName, WellKnownTypes.UInt16.FullyQualifiedName,
        WellKnownTypes.Int32.FullyQualifiedName, WellKnownTypes.UInt32.FullyQualifiedName,
        WellKnownTypes.Int64.FullyQualifiedName, WellKnownTypes.UInt64.FullyQualifiedName,
        WellKnownTypes.Char.FullyQualifiedName, WellKnownTypes.Single.FullyQualifiedName,
        WellKnownTypes.Double.FullyQualifiedName, WellKnownTypes.Decimal.FullyQualifiedName,
    ];

    public static bool Supports(IMistType type)
    {
        return type switch
        {
            RefinedType refined => Supports(refined.BaseType),
            EnumType { UnderlyingType: { } underlying } => Supports(underlying),
            _ => Array.IndexOf(ComparableNames, type.FullyQualifiedName) >= 0,
        };
    }

    public static bool IsIndeterminate(IMistType type)
    {
        return type is TypeParameter or UnknownType or UnknownValueType or ErrorType or NotProjectedType or LiftedType or SpreadType
            || (type is ITupleType tuple && tuple.ElementTypes.Any(element => element is SpreadType));
    }

    public static bool IsConstant(Expression bound)
    {
        return bound switch
        {
            LiteralExpression => true,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression } => true,
            MemberAccessExpression { Type: EnumType } => true,
            _ => false,
        };
    }

    public static bool HoldsConstant(IMistType input, Expression bound, TypeInterner? interner)
    {
        return ConversionClassifier.Classify(bound.Type, input, interner) is not ConversionKind.None
            || ConversionClassifier.Classify(input, bound.Type, interner) is not ConversionKind.None
            || (bound.Type.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName && Supports(input));
    }

    public static bool Compares(ComparisonPattern comparison, IMistType input, TypeInterner? interner)
    {
        if(IsIndeterminate(input) || input is UnionType || comparison.Bound.Type is ErrorType || !IsConstant(comparison.Bound))
        {
            return true;
        }

        return comparison.Kind == ComparisonKind.Equal
            ? HoldsConstant(input, comparison.Bound, interner)
            : Supports(input) && Supports(comparison.Bound.Type);
    }

    public static ComparisonPattern? Misordered(Pattern? predicate)
    {
        return predicate switch
        {
            ComparisonPattern { Kind: not ComparisonKind.Equal, NarrowedType: not ErrorType } comparison
                when !Supports(comparison.NarrowedType) => comparison,
            ConjunctionPattern conjunction => conjunction.Operands.Select(Misordered).FirstOrDefault(found => found is not null),
            DisjunctionPattern disjunction => disjunction.Operands.Select(Misordered).FirstOrDefault(found => found is not null),
            NegatedPattern negated => Misordered(negated.Operand),
            _ => null,
        };
    }

    public static ComparisonPattern? Mismatched(Pattern? predicate, TypeInterner? interner = null)
    {
        return predicate switch
        {
            ComparisonPattern { NarrowedType: not ErrorType } comparison
                when(comparison.Kind == ComparisonKind.Equal || Supports(comparison.NarrowedType))
                    && !Compares(comparison, comparison.NarrowedType, interner) => comparison,
            ConjunctionPattern conjunction => conjunction.Operands.Select(operand => Mismatched(operand, interner)).FirstOrDefault(found => found is not null),
            DisjunctionPattern disjunction => disjunction.Operands.Select(operand => Mismatched(operand, interner)).FirstOrDefault(found => found is not null),
            NegatedPattern negated => Mismatched(negated.Operand, interner),
            _ => null,
        };
    }

    public static bool IsRefused(IMistType type)
    {
        return type is RefinedType refined && (Misordered(refined.Predicate) is not null || Mismatched(refined.Predicate) is not null);
    }
}