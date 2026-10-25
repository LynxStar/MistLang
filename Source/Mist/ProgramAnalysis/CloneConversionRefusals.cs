using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Why a conversion run through a <c>Clone</c> body does not hold (newtypes spec Requirement 10.4), for the
/// cast rule to report where a cast calls it. Each body is judged on its own by every rule, with what they
/// find withheld: a finding there is a reason a cast is refused, not a mistake in code the program wrote,
/// and a private member of either type is the body's to reach (10.2).
/// </summary>
internal static class CloneConversionRefusals
{
    public static readonly IReadOnlyDictionary<CastOperator, ConversionRefusal> None = new Dictionary<CastOperator, ConversionRefusal>();

    public static IReadOnlyDictionary<CastOperator, ConversionRefusal> Judge(
        IEnumerable<SourceRoot> conversions, Func<SourceRoot, IEnumerable<Diagnostic>> analyze)
    {
        var refusals = new Dictionary<CastOperator, ConversionRefusal>();

        foreach(var root in conversions)
        {
            var conversion = (CastOperator)root.Member!;
            var boundary = AnalysisBoundary.ForBody(root);
            var failure = conversion.BindingFailure
                ?? analyze(root).FirstOrDefault(diagnostic => diagnostic.DefaultSeverity == DiagnosticSeverity.Error
                    && diagnostic.Number != ProgramAnalysisDiagnostics.InaccessibleMember.Number
                    && boundary.Contains(diagnostic));

            if(failure is not null)
            {
                refusals[conversion] = new ConversionRefusal(failure, MemberAt(BodyWalker.DescendantsAndSelf(root.Root), failure) ?? conversion.ClonesThrough!.Name);
            }
        }

        return refusals;
    }

    internal static string? MemberAt(IEnumerable<object> nodes, Diagnostic failure)
    {
        return nodes.Select<object, (SyntaxNode? Node, string? Name)>(node => node switch
            {
                Expression expression => (expression.Node, Name: Named(expression)),
                ObjectInitializerAssignment assignment => (assignment.Node, Name: assignment.MemberName),
                _ => (Node: null, Name: null),
            })
            .Where(candidate => candidate.Node is not null && candidate.Name is not null)
            .Select(candidate => (candidate.Name, DiagnosticHelper.ResolveLocation(candidate.Node!).Location))
            .Where(candidate => candidate.Location.Contains(failure.Location))
            .OrderBy(candidate => (candidate.Location as SourceRange)?.Length ?? 0)
            .Select(candidate => candidate.Name)
            .FirstOrDefault();
    }

    private static string? Named(Expression expression)
    {
        return expression switch
        {
            FieldReferenceExpression field => field.Field.Name,
            PropertyReferenceExpression property => property.Property.Name,
            ExtensionPropertyAccessExpression extension => extension.Property.Name,
            MemberAccessExpression access => access.MemberName,
            MethodReferenceExpression reference => reference.Method.Name,
            MethodGroupExpression group => group.MemberName,
            FunctionReferenceExpression function => function.Method.Name,
            InvocationExpression { Method: { } method } => method.Name,
            _ => null,
        };
    }
}

internal sealed record ConversionRefusal(Diagnostic Failure, string Member);