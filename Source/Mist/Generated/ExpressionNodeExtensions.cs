// Backward-compatible / dispatch helpers for the abstract ExpressionNode.
//
// The grammar changed from:
//   Expression = Assignment
// (which emitted a sealed ExpressionNode with a single .Assignment property)
// to:
//   Expression = lambda:LambdaExpression as LambdaForm
//              | assignment:Assignment   as AssignmentForm
// which makes ExpressionNode an ABSTRACT base with two sealed subtypes:
//   LambdaFormExpressionNode     (carries .Lambda  : LambdaExpressionNode)
//   AssignmentFormExpressionNode (carries .Assignment : AssignmentNode)
//
// The vast majority of existing consumers only ever expect the assignment form
// (lambda resolution/emission/inference is implemented by later feature tasks).
// These helpers let those call sites continue to reach the AssignmentNode via a
// single dispatch point, and provide a place to detect the lambda form so later
// tasks can fill in lambda handling without re-touching every call site.

namespace Mist.Generated;

/// <summary>
/// Dispatch helpers on the abstract <see cref="ExpressionNode"/> introduced when the
/// top-level <c>Expression</c> grammar rule was split into a lambda form and an
/// assignment form.
/// </summary>
public static class ExpressionNodeExtensions
{
    /// <summary>
    /// Returns the underlying <see cref="AssignmentNode"/> when this expression is the
    /// assignment form, or <c>null</c> when it is the lambda form.
    /// </summary>
    public static AssignmentNode? AsAssignmentOrNull(this ExpressionNode node)
    {
        return node switch
        {
            AssignmentFormExpressionNode assignmentForm => assignmentForm.Assignment,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the underlying <see cref="LambdaExpressionNode"/> when this expression is the
    /// lambda form, or <c>null</c> when it is the assignment form.
    /// </summary>
    public static LambdaExpressionNode? AsLambdaOrNull(this ExpressionNode node)
    {
        return node switch
        {
            LambdaFormExpressionNode lambdaForm => lambdaForm.Lambda,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the underlying <see cref="AssignmentNode"/>, treating the lambda form as an
    /// unsupported case. This is for call sites that, prior to the <c>Expression</c> split,
    /// unconditionally read <c>expression.Assignment</c> and for which lambda support is a
    /// later feature task. The lambda branch throws so that an unmigrated lambda path is
    /// surfaced loudly rather than silently misbehaving.
    /// </summary>
    public static AssignmentNode AsAssignment(this ExpressionNode node)
    {
        return node switch
        {
            AssignmentFormExpressionNode assignmentForm => assignmentForm.Assignment,
            LambdaFormExpressionNode => throw new System.NotSupportedException(
                "Lambda expressions are not yet supported at this stage of the compiler pipeline. "
                + "Lambda resolution, emission, and inference are implemented by later first-class-functions tasks."),
            _ => throw new System.InvalidOperationException(
                $"Unknown ExpressionNode variant: {node.GetType().Name}"),
        };
    }
}
