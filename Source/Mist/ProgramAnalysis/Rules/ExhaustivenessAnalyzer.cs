using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Report only proven missing values. <see cref="FiniteCoverage"/> gives no verdict for an unreadable arm;
/// on unbounded inputs only guarded or constant arms prove a gap, since finitely many constants cannot cover it.
/// A union constituent is missing only if no arm, even a guarded one, could match any of its values;
/// partial coverage is not reported.
/// <para>
/// A type test that differs from the type it meets only in a dependent slot compares the slot when the
/// program runs, and is emitted as a `when` (runtime-dependent-values Requirement 9.2), so it is read as a
/// guard: it covers that type only where the two slots are one refinement -- the same capture, literal,
/// refinement argument or parameter -- and otherwise leaves the type
/// to the other arms, so `Ring&lt;n&gt;` over `Ring&lt;m&gt; | string` leaves `Ring&lt;m&gt;` missing.
/// </para>
/// </summary>
internal sealed class ExhaustivenessAnalyzer : IProgramAnalyzer
{
    public string Id => "exhaustiveness-checking";

    public MistDiagnostic? OptionalRule => ProgramAnalysisDiagnostics.SwitchNotExhaustive;

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is SwitchExpression switchExpression
                && Missing(switchExpression, context.Model.TypeInterner) is { Count: > 0 } missing)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.SwitchNotExhaustive,
                    switchExpression.Node,
                    [switchExpression.Scrutinee],
                    new SequenceValue(SlotKind.Name, [.. missing.Select(item => new NameValue(item))]));
            }
        }
    }

    internal static MistDiagnostic? Finding(SwitchExpression switchExpression, TypeInterner? interner = null)
    {
        return Missing(switchExpression, interner) is { Count: > 0 } ? ProgramAnalysisDiagnostics.SwitchNotExhaustive : null;
    }

    internal static IReadOnlyList<string>? Missing(SwitchExpression switchExpression, TypeInterner? interner = null)
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
            UnionType union
                => [.. union.Constituents
                    .Where(constituent => !Flatten(arms.Select(arm => arm.Pattern)).Any(pattern => MightMatch(pattern, constituent, interner)))
                    .Select(TypeDisplay.Mist)],
            _ => FiniteCoverage.Missing(scrutinee, unguarded) ?? (OnlyConstants(arms, scrutinee) ? ["_"] : null),
        };
    }

    private static bool CoversAll(Pattern pattern, IMistType scrutinee)
    {
        return pattern is CatchAllPattern
            || (pattern is TypeTestPattern { Refinement: null } test
                && (ConversionRelation.IsIdentity(test.NarrowedType, scrutinee)
                    || (ConversionClassifier.AgreesUpToProof(scrutinee, test.NarrowedType) && !SlotIdentity.Compares(scrutinee, test.NarrowedType))));
    }

    private static bool MightMatch(Pattern pattern, IMistType constituent, TypeInterner? interner)
    {
        return pattern switch
        {
            TypeTestPattern test => (ReferenceEquals(test.Constituent ?? test.NarrowedType, constituent)
                || ConversionRelation.IsIdentity(test.Constituent ?? test.NarrowedType, constituent))
                && !SlotIdentity.Compares(constituent, test.NarrowedType),
            TuplePattern { Constituent: { } selected } => ReferenceEquals(selected, constituent) || ConversionRelation.IsIdentity(selected, constituent),
            ComparisonPattern comparison => !OptionalUnion.IsVoid(constituent) && Comparability.Compares(comparison, constituent, interner),
            _ => true,
        };
    }

    private static bool OnlyConstants(IReadOnlyList<SwitchArm> arms, IMistType scrutinee)
    {
        return arms.All(arm => arm.Guard is not null
            || Flatten([arm.Pattern]).All(pattern => pattern is ComparisonPattern { Kind: ComparisonKind.Equal }
                || (pattern is TypeTestPattern test && SlotIdentity.Compares(scrutinee, test.NarrowedType))));
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