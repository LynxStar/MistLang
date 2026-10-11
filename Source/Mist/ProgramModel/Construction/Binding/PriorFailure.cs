using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether a value already failed and was reported, which makes any complaint about it a second complaint
/// about one mistake.
/// <para>
/// Keyed on <see cref="ErrorExpression"/> rather than on ErrorType because the two sentinels mean opposite
/// things -- IdentifierBinder.ReportUnresolved records the distinction, and an
/// <see cref="UnresolvedExpression"/> is the compiler being unfinished with nothing yet reported, which
/// must still reach the message it has not had. A value built ON a failed one is the same one mistake,
/// and stays in the model as what it is so that the rest of it is still analysed; a local is the value it
/// was bound from, through <see cref="Model.LocalVariable.IsFailed"/>, and a refused member access is
/// itself one, through <see cref="MemberAccessExpression.IsFailed"/> -- which the binder records
/// because only it knows whether the refusal was spoken.
/// </para>
/// </summary>
internal static class PriorFailure
{
    public static bool Of(Expression value)
    {
        return value switch
        {
            ErrorExpression error => error.IsReported,
            LocalReferenceExpression reference => reference.Local.IsFailed,
            InvocationExpression call => Of(call.Receiver) || call.Arguments.Any(Of),
            MethodGroupExpression group => (group.Candidates.Count > 0 && group.Candidates.All(LocalFunctionBinder.IsRejected))
                || (group.Receiver is { } receiver && Of(receiver)),
            MethodReferenceExpression reference => LocalFunctionBinder.IsRejected(reference.Method),
            FunctionReferenceExpression reference => LocalFunctionBinder.IsRejected(reference.Method) || (reference.Receiver is { } receiver && Of(receiver)),
            MemberAccessExpression access => access.IsFailed || Of(access.Receiver),
            IndexExpression index => Of(index.Receiver) || Of(index.Index),
            ImplicitConversionExpression conversion => Of(conversion.Operand),
            UnaryExpression unary => Of(unary.Operand),
            BinaryExpression binary => Of(binary.Left) || Of(binary.Right),
            _ => false,
        };
    }
}