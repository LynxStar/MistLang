using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
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
            LiteralExpression { Value: { } constant } => constant.ToString() ?? "",
            UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd, Operand: var offset } => $"^{Display(offset)}",
            _ => TypeDisplay.Mist(expression.Type),
        };
    }

    private static string Describe(Requirement required)
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