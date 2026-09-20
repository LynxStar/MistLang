using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
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
/// <c>is</c> pattern is for.
/// </summary>
internal sealed class CastAnalyzer : IProgramAnalyzer
{
    public string Id => "total-conversion-classification";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is CastExpression cast && !Converts(context, cast))
            {
                Refuse(context, cast);
            }
        }
    }

    internal static bool Converts(AnalysisContext context, CastExpression cast)
    {
        var interner = context.Model.TypeInterner;

        if(cast.AmbiguousOperators.Count > 0)
        {
            return false;
        }

        if(cast.ParameterType is { } parameter)
        {
            return JudgeOperand(context, cast, parameter) is null && CloneRefusal(context, cast) is null;
        }

        return ConversionClassifier.Classify(cast.Operand.Type, cast.TargetType, interner) != ConversionKind.None
            || ConstantConversion.Fits(cast.Operand, cast.TargetType);
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
            if(JudgeOperand(context, cast, parameter) is null
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
    private static DiagnosticDescriptor? JudgeOperand(AnalysisContext context, CastExpression cast, IMistType parameter)
    {
        var interner = context.Model.TypeInterner;

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