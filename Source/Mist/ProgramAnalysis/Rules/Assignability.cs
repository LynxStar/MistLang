using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Whether a value may stand where a type is expected, decided once for every position that has one:
/// it may when the conversion is identity or implicit. Two things the classification alone would refuse
/// are admitted here. An integer constant is admitted into any integral type it fits, which is C#'s own
/// constant-expression conversion and what makes <c>byte b = 1</c> a program. And a target that is a
/// refinement over a type the value already belongs to is left alone, since whether the predicate holds
/// is the refinement prover's to discharge -- order 12 of the analysis register -- and a rule that judged
/// it here would refuse <c>AdultAge age = 20</c>, which classic admits.
/// </summary>
internal static class Assignability
{
    private static readonly Dictionary<string, (long Min, long Max)> IntegralRanges = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.SByte.FullyQualifiedName] = (sbyte.MinValue, sbyte.MaxValue),
        [WellKnownTypes.Byte.FullyQualifiedName] = (byte.MinValue, byte.MaxValue),
        [WellKnownTypes.Int16.FullyQualifiedName] = (short.MinValue, short.MaxValue),
        [WellKnownTypes.UInt16.FullyQualifiedName] = (ushort.MinValue, ushort.MaxValue),
        [WellKnownTypes.UInt32.FullyQualifiedName] = (uint.MinValue, uint.MaxValue),
        [WellKnownTypes.UInt64.FullyQualifiedName] = (0, long.MaxValue),
    };

    public static DiagnosticDescriptor? Judge(Expression value, IMistType target, TypeInterner interner)
    {
        return Judge(value.Type, target, interner, IntegerConstant(value));
    }

    public static DiagnosticDescriptor? Judge(IMistType from, IMistType to, TypeInterner interner, long? integerConstant = null)
    {
        var kind = ConversionClassifier.Classify(from, to, interner);

        if(kind is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified
            || AwaitsRefinementProof(from, to, interner))
        {
            return null;
        }

        if(kind == ConversionKind.Explicit)
        {
            return ProgramAnalysisDiagnostics.CastRequired;
        }

        return integerConstant is { } constant && Fits(constant, from, to)
            ? null
            : ProgramAnalysisDiagnostics.NoImplicitConversion
            ;
    }

    private static bool AwaitsRefinementProof(IMistType from, IMistType to, TypeInterner interner)
    {
        var constituents = to is UnionType union ? union.Constituents : [to];

        return constituents.Any(constituent => constituent is RefinedType refined
            && ConversionClassifier.Classify(from, Unrefined(refined), interner) is ConversionKind.Identity or ConversionKind.Implicit);
    }

    private static IMistType Unrefined(IMistType type)
    {
        return type is RefinedType refined ? Unrefined(refined.BaseType) : type;
    }

    private static bool Fits(long constant, IMistType from, IMistType to)
    {
        to = Unrefined(to);

        if(from.FullyQualifiedName == WellKnownTypes.Int64.FullyQualifiedName)
        {
            return to.FullyQualifiedName == WellKnownTypes.UInt64.FullyQualifiedName && constant >= 0;
        }

        return from.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName
            && IntegralRanges.TryGetValue(to.FullyQualifiedName, out var range)
            && constant >= range.Min
            && constant <= range.Max;
    }

    private static long? IntegerConstant(Expression expression)
    {
        return expression switch
        {
            LiteralExpression { Value: long value } => value,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate } negated => -IntegerConstant(negated.Operand),
            CloneExpression clone => IntegerConstant(clone.Source),
            _ => null,
        };
    }
}