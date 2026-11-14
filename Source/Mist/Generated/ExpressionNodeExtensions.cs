namespace Mist.Generated;

/// <summary>
/// <see cref="ExpressionNode"/> is either a lambda form or an assignment form; call sites that expect only the
/// assignment form reach it through these helpers.
/// </summary>
public static class ExpressionNodeExtensions
{
    public static AssignmentNode? AsAssignmentOrNull(this ExpressionNode node)
    {
        return node switch
        {
            AssignmentFormExpressionNode assignmentForm => assignmentForm.Assignment,
            _ => null,
        };
    }

    public static LambdaExpressionNode? AsLambdaOrNull(this ExpressionNode node)
    {
        return node switch
        {
            LambdaFormExpressionNode lambdaForm => lambdaForm.Lambda,
            _ => null,
        };
    }

    /// <summary>
    /// For call sites that cannot take a lambda: the lambda form throws, so a path that reaches one fails loudly
    /// instead of misreading it.
    /// </summary>
    public static AssignmentNode AsAssignment(this ExpressionNode node)
    {
        return node switch
        {
            AssignmentFormExpressionNode assignmentForm => assignmentForm.Assignment,
            LambdaFormExpressionNode => throw new NotSupportedException(
                "Lambda expressions are not yet supported at this stage of the compiler pipeline. "
                + "Lambda resolution, emission, and inference are implemented by later first-class-functions tasks."),
            _ => throw new InvalidOperationException(
                $"Unknown ExpressionNode variant: {node.GetType().Name}"),
        };
    }
}