using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Identifies inputs awaiting an analysis decision, without claiming they failed. ErrorType alone is
/// insufficient: an unfinished value must keep its fallback complaint if analysis finds no owner.
/// </summary>
internal static class FailureDependency
{
    public static bool NeedsAnalysis(Expression value)
    {
        var pending = value switch
        {
            ErrorExpression error => !error.IsReported,
            BinaryExpression binary => binary.Type is ErrorType,
            UnaryExpression unary => unary.Type is ErrorType,
            MemberAccessExpression access =>
                (access.IsConditional && access.Type is ErrorType)
                || (access.Receiver.Type is UnknownType && access.Member is null or NotFoundResult && access.Type is ErrorType),
            _ => false,
        };

        return pending || Inputs(value).Any(NeedsAnalysis);
    }

    public static IEnumerable<Expression> Inputs(Expression value)
    {
        return value switch
        {
            ErrorExpression error => error.Children,
            LocalReferenceExpression { Local.FailureSource: { } source } => [source],
            BinaryExpression binary => [binary.Left, binary.Right],
            UnaryExpression unary => [unary.Operand],
            MemberAccessExpression access => [access.Receiver],
            InvocationExpression call => [call.Receiver, .. call.Arguments],
            MethodGroupExpression { Receiver: { } receiver } => [receiver],
            FunctionReferenceExpression { Receiver: { } receiver } => [receiver],
            IndexExpression index => [index.Receiver, index.Index],
            ImplicitConversionExpression conversion => [conversion.Operand],
            NewExpression creation => [.. creation.Arguments, .. creation.Initializer.Select(item => item.Value)],
            CastExpression cast => [cast.Operand],
            AsExpression cast => [cast.Operand],
            AsyncExpression lift => [lift.Operand],
            CoalesceExpression coalesce => [coalesce.Left, coalesce.Right],
            AssignmentExpression assignment => [assignment.Target, assignment.Value],
            CloneExpression clone => [clone.Source],
            ExtensionPropertyAccessExpression access => [access.Receiver],
            ArrayCreationExpression creation => [creation.Size],
            ArrayLiteralExpression literal => literal.Elements,
            TupleLiteralExpression tuple => tuple.Elements,
            AnonymousObjectExpression anonymous => anonymous.Values,
            SpreadExpression spread => [spread.Operand],
            StackallocExpression allocated => [allocated.Size],
            RangeExpression range => new[] { range.Start, range.End }.OfType<Expression>(),
            InterpolatedStringExpression text => text.Parts.OfType<InterpolatedStringHole>().Select(hole => hole.Expression),
            SwitchExpression switchExpression => [switchExpression.Scrutinee, .. switchExpression.Arms.SelectMany(arm =>
                new[] { arm.Guard, arm.Result }.OfType<Expression>())],
            _ => [],
        };
    }
}