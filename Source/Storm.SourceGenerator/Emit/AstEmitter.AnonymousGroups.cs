using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.IR;

namespace Storm.SourceGenerator.Emit;

/// <summary>
/// The repetition or optional modifier applied to an anonymous group in a grammar
/// rule. Controls the C# property type projected onto the parent node: a scalar
/// group struct (<see cref="None"/>), a nullable group struct (<see cref="Optional"/>),
/// or an <c>ImmutableArray</c> of group structs (<see cref="ZeroOrMore"/> / <see cref="OneOrMore"/>).
/// </summary>
internal enum GroupModifier
{
    None,
    Optional,
    ZeroOrMore,
    OneOrMore,
}

/// <summary>
/// Describes a single anonymous group discovered within a rule alternative: the
/// group's body sequence, the modifier applied to it, its zero-based left-to-right
/// index among the alternative's anonymous groups, and the field labels declared
/// inside it.
/// </summary>
internal readonly record struct AnonymousGroupInfo(
    SequenceIR Sequence,
    GroupModifier Modifier,
    int GroupIndex,
    IReadOnlyList<FieldLabelIR> Labels);

public static partial class AstEmitter
{
    /// <summary>
    /// Detects anonymous groups in a rule alternative by walking its expression tree
    /// left-to-right. An anonymous group is a <see cref="SequenceIR"/> nested directly
    /// inside a <see cref="ZeroOrMoreIR"/>, <see cref="OneOrMoreIR"/>, or
    /// <see cref="OptionalIR"/> that contains at least one <see cref="FieldLabelIR"/>.
    /// Each detected group is assigned a distinct <see cref="AnonymousGroupInfo.GroupIndex"/>
    /// in source order, starting from zero.
    /// </summary>
    internal static List<AnonymousGroupInfo> DetectAnonymousGroups(RuleAlternativeIR alt)
    {
        var groups = new List<AnonymousGroupInfo>();
        var index = 0;
        WalkForGroups(alt.Expression, alt.FieldLabels, groups, ref index);
        return groups;
    }

    /// <summary>
    /// Determines whether an anonymous group qualifies for the Pure_Token_Alternation
    /// optimisation: the group has exactly one label whose expression is an
    /// <see cref="AlternationIR"/> where every alternative is a single
    /// <see cref="TokenRefIR"/>. When this holds, the group collapses to a single
    /// <c>Token</c>-typed property on the parent node rather than generating a
    /// dedicated group struct.
    /// </summary>
    /// <remarks>
    /// The design specifies a <c>SequenceIR seq</c> parameter alongside the labels,
    /// but the detection logic reads only the labels. An unused parameter is a build
    /// error under this project's analyzer configuration, so the redundant parameter
    /// is omitted while the logic remains identical to the design pseudocode.
    /// </remarks>
    internal static bool IsPureTokenAlternation(IReadOnlyList<FieldLabelIR> labelsInGroup)
    {
        if(labelsInGroup.Count != 1)
        {
            return false;
        }

        if(labelsInGroup[0].Expression is not AlternationIR alternation)
        {
            return false;
        }

        return alternation.Alternatives.All(alternative => alternative is TokenRefIR);
    }

    /// <summary>
    /// Recursively walks an expression tree, recording anonymous groups. When a
    /// repetition or optional wraps a labelled sequence, that sequence is recorded as
    /// a group and the walk does not descend into it -- nested groups are resolved
    /// separately during struct emission. Bare sequences are traversed element by
    /// element so groups nested deeper in the alternative are still found.
    /// </summary>
    private static void WalkForGroups(
        ExpressionIR expr,
        IReadOnlyList<FieldLabelIR> labels,
        List<AnonymousGroupInfo> groups,
        ref int index)
    {
        switch(expr)
        {
            case ZeroOrMoreIR { Inner: SequenceIR zeroOrMoreSeq } when HasLabelsInside(zeroOrMoreSeq, labels):
                groups.Add(new AnonymousGroupInfo(zeroOrMoreSeq, GroupModifier.ZeroOrMore, index++, GetLabelsInside(zeroOrMoreSeq, labels)));
                break;

            case OneOrMoreIR { Inner: SequenceIR oneOrMoreSeq } when HasLabelsInside(oneOrMoreSeq, labels):
                groups.Add(new AnonymousGroupInfo(oneOrMoreSeq, GroupModifier.OneOrMore, index++, GetLabelsInside(oneOrMoreSeq, labels)));
                break;

            case OptionalIR { Inner: SequenceIR optionalSeq } when HasLabelsInside(optionalSeq, labels):
                groups.Add(new AnonymousGroupInfo(optionalSeq, GroupModifier.Optional, index++, GetLabelsInside(optionalSeq, labels)));
                break;

            case SequenceIR seq:
                foreach(var element in seq.Elements)
                {
                    WalkForGroups(element, labels, groups, ref index);
                }

                break;
        }
    }

    /// <summary>
    /// Returns true when at least one of the alternative's field labels references an
    /// expression reachable within the given sequence.
    /// </summary>
    private static bool HasLabelsInside(SequenceIR seq, IReadOnlyList<FieldLabelIR> labels)
    {
        var reachable = new HashSet<ExpressionIR>();
        CollectReachableExpressions(seq, reachable);
        return labels.Any(label => reachable.Contains(label.Expression));
    }

    /// <summary>
    /// Returns the subset of the alternative's field labels whose expressions are
    /// reachable within the given sequence, preserving the labels' source order.
    /// </summary>
    private static IReadOnlyList<FieldLabelIR> GetLabelsInside(SequenceIR seq, IReadOnlyList<FieldLabelIR> labels)
    {
        var reachable = new HashSet<ExpressionIR>();
        CollectReachableExpressions(seq, reachable);
        return labels.Where(label => reachable.Contains(label.Expression)).ToList();
    }

    /// <summary>
    /// Adds the given expression and every descendant reachable through sequences,
    /// alternations, optionals, and repetitions to <paramref name="result"/>.
    /// Membership uses reference identity, matching how field labels reference the
    /// expression-node instances that live in the tree.
    /// </summary>
    private static void CollectReachableExpressions(ExpressionIR expr, HashSet<ExpressionIR> result)
    {
        if(!result.Add(expr))
        {
            return;
        }

        switch(expr)
        {
            case SequenceIR seq:
                foreach(var element in seq.Elements)
                {
                    CollectReachableExpressions(element, result);
                }

                break;

            case AlternationIR alternation:
                foreach(var alternative in alternation.Alternatives)
                {
                    CollectReachableExpressions(alternative, result);
                }

                break;

            case OptionalIR optional:
                CollectReachableExpressions(optional.Inner, result);
                break;

            case ZeroOrMoreIR zeroOrMore:
                CollectReachableExpressions(zeroOrMore.Inner, result);
                break;

            case OneOrMoreIR oneOrMore:
                CollectReachableExpressions(oneOrMore.Inner, result);
                break;
        }
    }
}