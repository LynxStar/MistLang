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

    public static SyntaxNode? AccessNodeOf(UnaryNode target)
    {
        return target is PostfixExpressionUnaryNode { Primary: var postfix } ? AccessNodeOf(postfix) : null;
    }

    public static SyntaxNode? AccessNodeOf(PostfixNode postfix)
    {
        return AccessNodeBefore(postfix, postfix.Suffix.Length);
    }

    /// <summary>
    /// The access that the suffix at <paramref name="suffixIndex"/> writes through, which is how a postfix
    /// increment names its target from inside the chain it ends.
    /// </summary>
    public static SyntaxNode? AccessNodeBefore(PostfixNode postfix, int suffixIndex)
    {
        if(suffixIndex > 0)
        {
            return postfix.Suffix[suffixIndex - 1];
        }

        if(postfix.Target is ParenthesizedExpressionPrimaryNode { Inner: var inner })
        {
            return inner.AsAssignmentOrNull() is { Group0: null } bare ? AccessNodeOf(bare.Target) : null;
        }

        return postfix.Target;
    }
}