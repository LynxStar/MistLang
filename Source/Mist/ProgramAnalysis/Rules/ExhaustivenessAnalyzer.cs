using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A switch expression some value of its scrutinee reaches no arm of.
/// <para>
/// IT REPORTS ONLY WHAT THE MODEL PROVES: a case is named missing when no unguarded arm could match it,
/// never merely because this rule cannot see how the arms cover it. Over a bool, an enum, an integral or a
/// tuple of them, <see cref="FiniteCoverage"/> searches the arms' value sets for a cell they leave --
/// samples/TuplePatternMatching switches over (int, int) with seven relational arms and no catch-all, and
/// is proven exhaustive rather than merely unrefused -- and gives no verdict when an arm has no reading,
/// which leaves anything unbounded reported only when every arm is guarded or a constant, since finitely
/// many constants cannot cover it. A union's constituent is missing when no arm, guarded or not, could
/// match a value of it: a type test of another constituent cannot, nor can a constant no value of the
/// constituent equals, which is how value arms over an optional leave `void` missing; a partially covered
/// constituent is not reported.
/// </para>
/// <para>
/// A type test that differs from the type it meets only in a dependent slot compares the slot when the
/// program runs, and is emitted as a `when` (runtime-dependent-values Requirement 9.2), so it is read as a
/// guard: it covers that type only where the two slots are one value -- the same capture, literal or
/// parameter, or two captures that read one holder its body never writes -- and otherwise leaves the type
/// to the other arms, so `Ring&lt;n&gt;` over `Ring&lt;m&gt; | string` leaves `Ring&lt;m&gt;` missing.
/// </para>
/// </summary>
internal sealed class ExhaustivenessAnalyzer : IProgramAnalyzer
{
    public string Id => "exhaustiveness-checking";

    public MistDiagnostic? OptionalRule => ProgramAnalysisDiagnostics.SwitchNotExhaustive;

    public void Analyze(AnalysisContext context)
    {
        var writes = new Dictionary<object, IReadOnlySet<object>>(ReferenceEqualityComparer.Instance);

        foreach(var node in context.Nodes)
        {
            if(node is SwitchExpression switchExpression
                && Missing(switchExpression, context.Model.TypeInterner, Unwritten(context.RootOf(switchExpression), writes)) is { Count: > 0 } missing)
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

    internal static IReadOnlyList<string>? Missing(
        SwitchExpression switchExpression, TypeInterner? interner = null, Func<CapturedValue, CapturedValue, bool>? oneValue = null)
    {
        var scrutinee = switchExpression.Scrutinee.Type;
        var arms = switchExpression.Arms;
        oneValue ??= (_, _) => false;

        if(arms.Any(arm => arm.Pattern.NarrowedType is ErrorType))
        {
            return null;
        }

        var unguarded = Flatten(arms.Where(arm => arm.Guard is null).Select(arm => arm.Pattern)).ToList();

        if(unguarded.Any(pattern => CoversAll(pattern, scrutinee, oneValue)))
        {
            return null;
        }

        return scrutinee switch
        {
            UnionType union
                => [.. union.Constituents
                    .Where(constituent => !Flatten(arms.Select(arm => arm.Pattern)).Any(pattern => MightMatch(pattern, constituent, interner, oneValue)))
                    .Select(TypeDisplay.Mist)],
            _ => FiniteCoverage.Missing(scrutinee, unguarded) ?? (OnlyConstants(arms, scrutinee, oneValue) ? ["_"] : null),
        };
    }

    private static bool CoversAll(Pattern pattern, IMistType scrutinee, Func<CapturedValue, CapturedValue, bool> oneValue)
    {
        return pattern is CatchAllPattern
            || (pattern is TypeTestPattern { Refinement: null } test
                && (ConversionRelation.IsIdentity(test.NarrowedType, scrutinee)
                    || (ConversionClassifier.AgreesUpToProof(scrutinee, test.NarrowedType) && !SlotIdentity.Compares(scrutinee, test.NarrowedType, oneValue))));
    }

    private static bool MightMatch(Pattern pattern, IMistType constituent, TypeInterner? interner, Func<CapturedValue, CapturedValue, bool> oneValue)
    {
        return pattern switch
        {
            TypeTestPattern test => (ReferenceEquals(test.Constituent ?? test.NarrowedType, constituent)
                || ConversionRelation.IsIdentity(test.Constituent ?? test.NarrowedType, constituent))
                && !SlotIdentity.Compares(constituent, test.NarrowedType, oneValue),
            TuplePattern { Constituent: { } selected } => ReferenceEquals(selected, constituent) || ConversionRelation.IsIdentity(selected, constituent),
            ComparisonPattern comparison => !OptionalUnion.IsVoid(constituent) && Comparability.Compares(comparison, constituent, interner),
            _ => true,
        };
    }

    private static bool OnlyConstants(IReadOnlyList<SwitchArm> arms, IMistType scrutinee, Func<CapturedValue, CapturedValue, bool> oneValue)
    {
        return arms.All(arm => arm.Guard is not null
            || Flatten([arm.Pattern]).All(pattern => pattern is ComparisonPattern { Kind: ComparisonKind.Equal }
                || (pattern is TypeTestPattern test && SlotIdentity.Compares(scrutinee, test.NarrowedType, oneValue))));
    }

    private static Func<CapturedValue, CapturedValue, bool>? Unwritten(object? root, Dictionary<object, IReadOnlySet<object>> writes)
    {
        if(root is null)
        {
            return null;
        }

        if(!writes.TryGetValue(root, out var written))
        {
            var holders = new HashSet<object>(UnseenWrites.Of(root), ReferenceEqualityComparer.Instance);

            foreach(var node in BodyWalker.DescendantsAndSelf(root))
            {
                Assigned(holders, node switch
                {
                    AssignmentExpression assignment => assignment.Target,
                    CompoundAssignmentExpression compound => compound.Target,
                    _ => null,
                });
            }

            writes[root] = written = holders;
        }

        return (left, right) => SlotReads.Written(left.Capture.Initializer) is LocalReferenceExpression { Local: var holder }
            && SlotReads.Written(right.Capture.Initializer) is LocalReferenceExpression { Local: var other }
            && ReferenceEquals(holder, other)
            && !written.Contains(holder);
    }

    private static void Assigned(HashSet<object> holders, Expression? target)
    {
        switch(target)
        {
            case LocalReferenceExpression local:
                _ = holders.Add(local.Local);
                break;

            case TupleLiteralExpression tuple:
                foreach(var element in tuple.Elements)
                {
                    Assigned(holders, element);
                }

                break;
        }
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