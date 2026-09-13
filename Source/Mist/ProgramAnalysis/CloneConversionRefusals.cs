using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

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
            var failure = conversion.BindingFailure
                ?? analyze(root).FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                    && diagnostic.Id != ProgramAnalysisDiagnostics.InaccessibleMember.Id);

            if(failure is not null)
            {
                refusals[conversion] = new ConversionRefusal(failure, MemberAt(root, failure) ?? conversion.ClonesThrough!.Name);
            }
        }

        return refusals;
    }

    private static string? MemberAt(SourceRoot root, Diagnostic failure)
    {
        return BodyWalker.DescendantsAndSelf(root.Root)
            .OfType<Expression>()
            .Where(expression => DiagnosticHelper.ResolveLocation(expression.Node).Location == failure.Location)
            .Select(Named)
            .FirstOrDefault(name => name is not null);
    }

    private static string? Named(Expression expression)
    {
        return expression switch
        {
            FieldReferenceExpression field => field.Field.Name,
            PropertyReferenceExpression property => property.Property.Name,
            MemberAccessExpression access => access.MemberName,
            InvocationExpression { Method: { } method } => method.Name,
            _ => null,
        };
    }
}

internal sealed record ConversionRefusal(Diagnostic Failure, string Member);