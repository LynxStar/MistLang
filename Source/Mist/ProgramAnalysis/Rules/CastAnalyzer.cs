using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A cast with no conversion to perform. Mist admits a cast only to a type the value already is, or to
/// one whose conversion function -- a declared cast operator, the cast-operators spec's Requirement 8, or
/// an enum's lookup -- the value can be passed to, so that no cast can fail at runtime. That rules
/// out every conversion C# would check when it ran: a downcast, a numeric narrowing, a union narrowed
/// to one constituent, a value typed <c>unknown</c> narrowed to anything. Narrowing is what an
/// <c>is</c> pattern is for. A pure function type is a promise the emitted program erases, so a cast
/// or an <c>as</c> that would have the runtime test for one -- directly, in a union constituent or in
/// a type argument -- is refused unless the operand's static type already states that same type
/// (purity Requirement 16.5); <see cref="PatternAnalyzer"/> asks <see cref="HidesPurity"/> of a type
/// test for the same reason, and at every closing of a type parameter the target names and the
/// operand's type does not.
/// </summary>
internal sealed class CastAnalyzer : IProgramAnalyzer
{
    public string Id => "total-conversion-classification";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case CastExpression cast when !Converts(context, cast):
                    Refuse(context, cast);
                    break;

                case CastExpression cast when Tests(context, cast) && HidesPurity(cast.Operand.Type, cast.TargetType):
                    RefuseHiddenPurity(context, cast, cast.Operand, cast.TargetType);
                    break;

                case AsExpression { Kind: AsConversionKind.Narrowing or AsConversionKind.UnionExtraction } converted
                    when HidesPurity(converted.Operand.Type, converted.TargetType):
                    RefuseHiddenPurity(context, converted, converted.Operand, converted.TargetType);
                    break;
            }
        }
    }

    internal static bool Tests(AnalysisContext context, CastExpression cast)
    {
        return cast.ParameterType is null
            && ConversionClassifier.Classify(cast.Operand.Type, cast.TargetType, context.Model.TypeInterner)
                is not (ConversionKind.Identity or ConversionKind.Implicit);
    }

    internal static bool HidesPurity(IMistType from, IMistType to)
    {
        var stated = PureFunctions(from).Select(function => function.FullyQualifiedName).ToHashSet(StringComparer.Ordinal);

        return PureFunctions(to).Any(promised => !stated.Contains(promised.FullyQualifiedName));
    }

    internal static IFunctionType? PureFunctionIn(IMistType type)
    {
        return PureFunctions(type).FirstOrDefault();
    }

    internal static IEnumerable<TypeParameter> HiddenParameters(IMistType from, IMistType to)
    {
        var seen = new HashSet<TypeParameter>(TypeParameters(from), ReferenceEqualityComparer.Instance);

        return TypeParameters(to).Where(seen.Add).ToList();
    }

    internal static IEnumerable<TypeParameter> TypeParameters(IMistType type)
    {
        if(type is TypeParameter parameter)
        {
            yield return parameter;
        }

        if(type is ConstructedType { Definition: TypeParameter definition })
        {
            yield return definition;
        }

        foreach(var inner in Nested(type).SelectMany(TypeParameters))
        {
            yield return inner;
        }
    }

    private static IEnumerable<IFunctionType> PureFunctions(IMistType type)
    {
        if(type is IFunctionType { IsPure: true } pure)
        {
            yield return pure;
        }

        foreach(var inner in Nested(type).SelectMany(PureFunctions))
        {
            yield return inner;
        }
    }

    private static IEnumerable<IMistType> Nested(IMistType type)
    {
        return type switch
        {
            IFunctionType function => function.ParameterTypes.Append(function.ReturnType),
            UnionType union => union.Constituents,
            ConstructedType constructed => constructed.TypeArguments,
            LiftedType lifted => [lifted.Type],
            INamedTupleType named => [named.Underlying],
            ITupleType tuple => tuple.ElementTypes,
            TypeAlias { AliasedType: { } aliased } => [aliased],
            RefinedType refined => [refined.BaseType],
            _ => [],
        };
    }

    private static void RefuseHiddenPurity(AnalysisContext context, Expression at, Expression operand, IMistType target)
    {
        context.Report(
            ProgramAnalysisDiagnostics.PureFunctionTypeUntestable,
            at.Node,
            [at, operand],
            TypeDisplay.Mist(operand.Type),
            TypeDisplay.Mist(target));
    }

    internal static bool Converts(AnalysisContext context, CastExpression cast)
    {
        return Judge(cast, context.Model.TypeInterner, context.ConversionRefusals) is null;
    }

    internal static DiagnosticDescriptor? Judge(
        CastExpression cast, TypeInterner interner, IReadOnlyDictionary<CastOperator, ConversionRefusal> refusals)
    {
        if(cast.AmbiguousOperators.Count > 0)
        {
            return ProgramAnalysisDiagnostics.AmbiguousCast;
        }

        if(cast.ParameterType is { } parameter)
        {
            if(JudgeOperand(cast, parameter, interner) is not null)
            {
                return cast.Operator is null ? ProgramAnalysisDiagnostics.EnumCastArgumentMismatch : ProgramAnalysisDiagnostics.CastArgumentMismatch;
            }

            return cast.Operator is { ClonesThrough: not null } through && refusals.ContainsKey(through)
                ? ProgramAnalysisDiagnostics.CloneConversionDoesNotHold
                : null;
        }

        if(ConversionClassifier.Classify(cast.Operand.Type, cast.TargetType, interner) != ConversionKind.None
            || ConstantConversion.Fits(cast.Operand, cast.TargetType))
        {
            return null;
        }

        return UnheldCopies.Exists(cast.Operand.Type, cast.TargetType)
            ? ProgramAnalysisDiagnostics.CopyConversionDoesNotHold
            : UncopiedSource(cast.Operand.Type, cast.TargetType) is not null
                ? ProgramAnalysisDiagnostics.NewtypeSourceDefinesNoCopy
                : ProgramAnalysisDiagnostics.CastHasNoConversion;
    }

    private static void Refuse(AnalysisContext context, CastExpression cast)
    {
        if(cast.AmbiguousOperators.Count > 0)
        {
            context.Report(
                ProgramAnalysisDiagnostics.AmbiguousCast,
                cast.Node,
                [cast, cast.Operand],
                TypeDisplay.Mist(cast.Operand.Type),
                TypeDisplay.Mist(cast.TargetType),
                string.Join(", ", cast.AmbiguousOperators.Select(Signature)));
        }
        else if(cast.ParameterType is { } parameter)
        {
            if(JudgeOperand(cast, parameter, context.Model.TypeInterner) is null
                && CloneRefusal(context, cast) is { } refusal && cast.Operator is { ClonesThrough: { } body })
            {
                context.Report(
                    ProgramAnalysisDiagnostics.CloneConversionDoesNotHold,
                    cast.Node,
                    [cast, cast.Operand],
                    TypeDisplay.Mist(cast.Operand.Type),
                    TypeDisplay.Mist(cast.TargetType),
                    $"{TypeDisplay.Mist(body.DeclaringType!)}.{body.Name}",
                    TypeDisplay.Mist(parameter),
                    refusal.Member,
                    refusal.Failure.Message);
            }
            else if(cast.Operator is { } called)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.CastArgumentMismatch,
                    cast.Node,
                    [cast, cast.Operand],
                    TypeDisplay.Mist(cast.Operand.Type),
                    TypeDisplay.Mist(cast.TargetType),
                    Signature(called),
                    TypeDisplay.Mist(parameter));
            }
            else
            {
                context.Report(
                    ProgramAnalysisDiagnostics.EnumCastArgumentMismatch,
                    cast.Node,
                    [cast, cast.Operand],
                    TypeDisplay.Mist(cast.Operand.Type),
                    TypeDisplay.Mist(cast.TargetType),
                    TypeDisplay.Mist(parameter));
            }
        }
        else if(!UnheldCopies.Report(context, cast.Operand.Type, cast.TargetType, cast.Node, [cast, cast.Operand]))
        {
            RefuseUnconverted(context, cast);
        }
    }

    private static void RefuseUnconverted(AnalysisContext context, CastExpression cast)
    {
        if(UncopiedSource(cast.Operand.Type, cast.TargetType) is { } source)
        {
            context.Report(
                ProgramAnalysisDiagnostics.NewtypeSourceDefinesNoCopy,
                cast.Node,
                [cast, cast.Operand],
                TypeDisplay.Mist(cast.Operand.Type),
                TypeDisplay.Mist(cast.TargetType),
                TypeDisplay.Mist(source));
        }
        else
        {
            context.Report(
                ProgramAnalysisDiagnostics.CastHasNoConversion,
                cast.Node,
                [cast, cast.Operand],
                TypeDisplay.Mist(cast.Operand.Type),
                TypeDisplay.Mist(cast.TargetType));
        }
    }

    /// <summary>
    /// A conditional conversion's operand is judged by what it holds when it is present: absence
    /// short-circuits past the operator rather than reaching its parameter.
    /// </summary>
    private static DiagnosticDescriptor? JudgeOperand(CastExpression cast, IMistType parameter, TypeInterner interner)
    {
        return cast.IsConditional
            ? Assignability.Judge(OptionalLifting.NonVoidPortion(cast.Operand.Type, interner), parameter, interner)
            : Assignability.Judge(cast.Operand, parameter, interner)
            ;
    }

    private static ConversionRefusal? CloneRefusal(AnalysisContext context, CastExpression cast)
    {
        return cast.Operator is { ClonesThrough: not null } through && context.ConversionRefusals.TryGetValue(through, out var refusal) ? refusal : null;
    }

    private static IMistType? UncopiedSource(IMistType from, IMistType to)
    {
        return Uncopied(from, to) ?? Uncopied(to, from);
    }

    private static IMistType? Uncopied(IMistType clone, IMistType other)
    {
        var definition = clone is ConstructedType constructed ? constructed.Definition : clone;

        return definition is ConcreteType { CloneSource: { } source }
            && !NewtypeConversions.IsCopyable(source)
            && ConversionRelation.CloneSourceOf(clone) is { } sourceType
            && ConversionRelation.IsIdentity(other, sourceType)
            ? sourceType
            : null;
    }

    internal static string Signature(CastOperator castOperator)
    {
        var declaring = castOperator.DeclaringType is { } type ? $"{TypeDisplay.Mist(type)}." : "";
        var kind = castOperator.IsImplicit ? "implicit" : "explicit";

        return $"'{declaring}{kind} operator {TypeDisplay.Mist(castOperator.TargetType)}({TypeDisplay.Mist(castOperator.SourceParameter.Type)})'";
    }
}