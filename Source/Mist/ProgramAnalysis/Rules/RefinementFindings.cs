using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class RefinementFindings
{
    public static void Report(
        AnalysisContext context, Admitted known, Expression value, Requirement required, string target, Expression at,
        string? onConstraint, Expression? onIndex = null)
    {
        var predicate = Describe(required);

        if(onIndex is not null)
        {
            if(known.Intervals is [var only] && only.Low == only.High && !required.Values.Contains(only.Low))
            {
                context.Report(ProgramAnalysisDiagnostics.IndexOutOfBounds, at.Node, [value, onIndex], only.Low, target, predicate);
            }
            else
            {
                context.Report(ProgramAnalysisDiagnostics.IndexUnproven, at.Node, [value, onIndex], Display(value), target, predicate);
            }
        }
        else if(onConstraint is not null)
        {
            if(Violating(value, known, required) is { } constant)
            {
                context.Report(ProgramAnalysisDiagnostics.ConstraintViolated, at.Node, [value, at], constant, predicate, onConstraint);
            }
            else
            {
                context.Report(ProgramAnalysisDiagnostics.ConstraintUnproven, at.Node, [value, at], Display(value), predicate, onConstraint);
            }
        }
        else if(Violating(value, known, required) is { } constant)
        {
            context.Report(ProgramAnalysisDiagnostics.RefinementViolated, at.Node, [value, at], constant, predicate, target);
        }
        else if(Requirement.Refined(value.Type) is not null)
        {
            context.Report(ProgramAnalysisDiagnostics.RefinementNotImplied, at.Node, [value, at], TypeDisplay.Mist(value.Type), target, known, predicate);
        }
        else
        {
            context.Report(ProgramAnalysisDiagnostics.RefinementUnproven, at.Node, [value, at], TypeDisplay.Mist(value.Type), target, predicate);
        }
    }

    public static void ReportPosition(
        AnalysisContext context, Admitted position, Requirement required, Storm.Runtime.SyntaxNode at, string shown, string parameter,
        IReadOnlyList<Expression> about)
    {
        var predicate = Describe(required);

        if(Lengths.Exact(position) is { } only && !required.Values.Contains(only))
        {
            context.Report(ProgramAnalysisDiagnostics.ConstraintViolated, at, about, only, predicate, parameter);
        }
        else
        {
            context.Report(ProgramAnalysisDiagnostics.ConstraintUnproven, at, about, shown, predicate, parameter);
        }
    }

    public static void ReportUnreadable(
        AnalysisContext context, Expression value, IMistType constituent, string target, Expression at, string? onConstraint)
    {
        var predicate = Describe(constituent);

        if(onConstraint is not null)
        {
            context.Report(ProgramAnalysisDiagnostics.ConstraintUnproven, at.Node, [value, at], Display(value), predicate, onConstraint);
        }
        else
        {
            context.Report(ProgramAnalysisDiagnostics.RefinementUnproven, at.Node, [value, at], TypeDisplay.Mist(value.Type), target, predicate);
        }
    }

    public static void ReportLiteral(
        AnalysisContext context, Expression value, IMistType constituent, string target, Expression at, string? onConstraint,
        LiteralValue? literal)
    {
        if(LiteralPredicates.Decide(constituent, literal) != false)
        {
            ReportUnreadable(context, value, constituent, target, at, onConstraint);
            return;
        }

        context.Report(
            onConstraint is null ? ProgramAnalysisDiagnostics.RefinementViolated : ProgramAnalysisDiagnostics.ConstraintViolated,
            at.Node, [value, at], literal!, Describe(constituent), onConstraint ?? target);
    }

    public static void ReportPositionUnreadable(
        AnalysisContext context, IMistType slot, Storm.Runtime.SyntaxNode at, string shown, string parameter, IReadOnlyList<Expression> about)
    {
        context.Report(ProgramAnalysisDiagnostics.ConstraintUnproven, at, about, shown, Describe(slot), parameter);
    }

    private static string Describe(IMistType constituent)
    {
        return Requirement.Refined(constituent) is RefinedType refined
            ? Requirement.Refined(refined.BaseType) is null ? Describe(refined.Predicate) : $"{Describe(refined.BaseType)} and {Describe(refined.Predicate)}"
            : TypeDisplay.Mist(constituent);
    }

    private static string Describe(Pattern? pattern)
    {
        return pattern switch
        {
            ComparisonPattern { Kind: ComparisonKind.Equal, Bound: var bound } => Shown(bound),
            ComparisonPattern { Kind: var kind, Bound: var bound } => $"{Symbol(kind)} {Shown(bound)}",
            ConjunctionPattern conjunction => string.Join(" and ", conjunction.Operands.Select(Describe)),
            DisjunctionPattern disjunction => $"({string.Join(" or ", disjunction.Operands.Select(Describe))})",
            NegatedPattern negated => $"not {Describe(negated.Operand)}",
            PropertyPattern property => $"{{ {string.Join(", ", property.Subpatterns.Select(sub => $"{sub.MemberName}: {Describe(sub.Pattern)}"))} }}",
            RegexPattern regex => $"`{regex.Pattern.Replace("\\\"", "\"").Replace("\\\\", "\\")}`",
            TypeTestPattern { Refinement: { } refinement } test => $"{TypeDisplay.Mist(test.NarrowedType)} {Describe(refinement)}",
            TypeTestPattern test => TypeDisplay.Mist(test.NarrowedType),
            _ => "_",
        };
    }

    private static string Shown(Expression bound)
    {
        return bound is LiteralExpression { Value: string text } ? $"\"{text}\"" : Display(bound);
    }

    private static long? Violating(Expression value, Admitted known, Requirement required)
    {
        return (Requirement.Constant(value) ?? Lengths.Exact(known)) is { } only && !required.Values.Contains(only) ? only : null;
    }

    public static string Display(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression local => local.Local.Name,
            FieldReferenceExpression field => field.Field.Name,
            ThisExpression => "this",
            MemberAccessExpression access => $"{Display(access.Receiver)}.{access.MemberName}",
            LiteralExpression { Value: { } constant } => Convert.ToString(constant, CultureInfo.InvariantCulture) ?? "",
            UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd, Operand: var offset } => $"^{Display(offset)}",
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression { Value: not null } negated } => $"-{Display(negated)}",
            _ => TypeDisplay.Mist(expression.Type),
        };
    }

    public static string Describe(Requirement required)
    {
        var parts = new List<string>();

        if(!required.Values.IsEverything)
        {
            parts.Add(required.Values.ToString());
        }

        foreach(var bound in required.Bounds)
        {
            parts.Add($"{Symbol(bound.Kind)} {bound.Name}");
        }

        return parts.Count == 0 ? "anything" : string.Join(" and ", parts);
    }

    private static string Symbol(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => "<",
            ComparisonKind.LessThanOrEqual => "<=",
            ComparisonKind.GreaterThan => ">",
            ComparisonKind.GreaterThanOrEqual => ">=",
            _ => "==",
        };
    }
}