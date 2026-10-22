using Mist.Generated;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// The syntax of the one access an assignment writes through: the last suffix of a bare postfix target,
/// or its primary where it has none. Anything else -- an operator chain, a range -- is not storage, and a
/// write check at any access inside it would complain about a program that is wrong for another reason.
/// </summary>
internal static class AssignmentTargets
{
    public static SyntaxNode? AccessNodeOf(VoidCoalescingNode target)
    {
        if(!OperatorBinder.IsBare(target.LogicalOrs)
            || !OperatorBinder.IsBare(target.Left.LogicalAnds)
            || !OperatorBinder.IsBare(target.Left.Left.Equalitys)
            || target.Left.Left.Left is not { Group0.IsDefaultOrEmpty: true, Left: RelationalComparisonRelationalNode { Group0.IsDefaultOrEmpty: true, Left: { Group0.IsDefaultOrEmpty: true, Left: { Group0.IsDefaultOrEmpty: true, Left: NonRangeRangeNode { Operand: PostfixExpressionUnaryNode { Primary: var postfix } } } } } })
        {
            return null;
        }

        return AccessNodeOf(postfix);
    }

    private static SyntaxNode? AccessNodeOf(PostfixNode postfix)
    {
        if(postfix.Suffix.Length > 0)
        {
            return postfix.Suffix[^1];
        }

        if(postfix.Target is ParenthesizedExpressionPrimaryNode { Inner: var inner })
        {
            return inner.AsAssignmentOrNull() is { Group0: null } bare ? AccessNodeOf(bare.Target) : null;
        }

        return postfix.Target;
    }
}