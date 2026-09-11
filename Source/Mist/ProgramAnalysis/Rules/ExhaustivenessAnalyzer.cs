using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A switch expression some value of its scrutinee reaches no arm of. Order 3 of the analysis register,
/// and the contract's first customer.
/// <para>
/// IT REPORTS ONLY WHAT THE MODEL PROVES: a case is named missing when no arm could match it at all,
/// never merely because this rule cannot see how the arms cover it. Not caution for its own sake --
/// samples/TuplePatternMatching switches over (int, int) with nine relational arms and no catch-all,
/// and is exhaustive, but deciding so needs range reasoning this rule does not have. A rule that guessed
/// would refuse a working program. So no tuple arm is ever counted as covering, a partially covered
/// union constituent is not reported, and anything unbounded -- a tuple included -- is reported only
/// when every arm is guarded or a constant, since finitely many constants cannot cover it.
/// </para>
/// </summary>
internal sealed class ExhaustivenessAnalyzer : IProgramAnalyzer
{
    public string Id => "exhaustiveness-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is SwitchExpression switchExpression && Missing(switchExpression) is { Count: > 0 } missing)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.SwitchNotExhaustive,
                    switchExpression.Node,
                    [switchExpression.Scrutinee],
                    string.Join(", ", missing));
            }
        }
    }

    private static IReadOnlyList<string>? Missing(SwitchExpression switchExpression)
    {
        var scrutinee = switchExpression.Scrutinee.Type;
        var arms = switchExpression.Arms;

        if(arms.Any(arm => arm.Pattern.NarrowedType is ErrorType))
        {
            return null;
        }

        var unguarded = Flatten(arms.Where(arm => arm.Guard is null).Select(arm => arm.Pattern)).ToList();

        if(unguarded.Any(pattern => CoversAll(pattern, scrutinee)))
        {
            return null;
        }

        return scrutinee switch
        {
            _ when scrutinee.FullyQualifiedName == WellKnownTypes.Boolean.FullyQualifiedName
                => MissingValues(unguarded, ["true", "false"], BoolCase),
            EnumType enumType
                => MissingValues(unguarded, [.. enumType.MembersList.Select(member => member.Name)], VariantCase),
            UnionType union
                => [.. union.Constituents
                    .Where(constituent => !Flatten(arms.Select(arm => arm.Pattern)).Any(pattern => MightMatch(pattern, constituent)))
                    .Select(TypeDisplay.Mist)],
            _ => OnlyConstants(arms) ? ["_"] : null,
        };
    }

    private static bool CoversAll(Pattern pattern, IMistType scrutinee)
    {
        return pattern is CatchAllPattern
            || (pattern is TypeTestPattern { Refinement: null } test
                && ConversionRelation.IsIdentity(test.NarrowedType, scrutinee));
    }

    private static IReadOnlyList<string>? MissingValues(
        List<Pattern> unguarded, IReadOnlyList<string> cases, Func<Pattern, string?> caseOf)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach(var pattern in unguarded)
        {
            if(caseOf(pattern) is not { } value)
            {
                return null;
            }

            _ = covered.Add(value);
        }

        return [.. cases.Where(value => !covered.Contains(value))];
    }

    private static string? BoolCase(Pattern pattern)
    {
        return pattern switch
        {
            ComparisonPattern { Kind: ComparisonKind.Equal, Bound: LiteralExpression { Value: true } } => "true",
            ComparisonPattern { Kind: ComparisonKind.Equal, Bound: LiteralExpression { Value: false } } => "false",
            _ => null,
        };
    }

    private static string? VariantCase(Pattern pattern)
    {
        return pattern is ComparisonPattern
        {
            Kind: ComparisonKind.Equal, Bound: MemberAccessExpression { Type: EnumType } variant,
        }
            ? variant.MemberName
            : null
            ;
    }

    private static bool MightMatch(Pattern pattern, IMistType constituent)
    {
        return pattern is not TypeTestPattern test
            || ConversionRelation.IsIdentity(test.Constituent ?? test.NarrowedType, constituent);
    }

    private static bool OnlyConstants(IReadOnlyList<SwitchArm> arms)
    {
        return arms.All(arm => arm.Guard is not null
            || Flatten([arm.Pattern]).All(pattern => pattern is ComparisonPattern { Kind: ComparisonKind.Equal }));
    }

    private static IEnumerable<Pattern> Flatten(IEnumerable<Pattern> patterns)
    {
        foreach(var pattern in patterns)
        {
            if(pattern is DisjunctionPattern disjunction)
            {
                foreach(var operand in Flatten(disjunction.Operands))
                {
                    yield return operand;
                }
            }
            else
            {
                yield return pattern;
            }
        }
    }
}