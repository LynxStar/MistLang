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
        var interner = context.Model.TypeInterner;

        foreach(var node in context.Nodes)
        {
            if(node is not CastExpression cast)
            {
                continue;
            }

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
                if(Assignability.Judge(cast.Operand, parameter, interner) is null)
                {
                    if(cast.Operator is { ClonesThrough: { } body } through && context.ConversionRefusals.TryGetValue(through, out var refusal))
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

                    continue;
                }

                if(cast.Operator is { } called)
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
            else if(ConversionClassifier.Classify(cast.Operand.Type, cast.TargetType, interner) != ConversionKind.None)
            {
                continue;
            }
            else if(UncopiedSource(cast.Operand.Type, cast.TargetType) is { } source)
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
    }

    private static IMistType? UncopiedSource(IMistType from, IMistType to)
    {
        return (from, to) switch
        {
            (ConcreteType { CloneSource: { } source, CloneSourceType: { } sourceType }, _)
                when ConversionRelation.IsIdentity(to, sourceType) && !NewtypeConversions.IsCopyable(source) => sourceType,
            (_, ConcreteType { CloneSource: { } source, CloneSourceType: { } sourceType })
                when ConversionRelation.IsIdentity(from, sourceType) && !NewtypeConversions.IsCopyable(source) => sourceType,
            _ => null,
        };
    }

    internal static string Signature(CastOperator castOperator)
    {
        var declaring = castOperator.DeclaringType is { } type ? $"{TypeDisplay.Mist(type)}." : "";
        var kind = castOperator.IsImplicit ? "implicit" : "explicit";

        return $"'{declaring}{kind} operator {TypeDisplay.Mist(castOperator.TargetType)}({TypeDisplay.Mist(castOperator.SourceParameter.Type)})'";
    }
}