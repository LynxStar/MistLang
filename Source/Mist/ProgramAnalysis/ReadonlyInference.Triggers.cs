using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal sealed partial class ReadonlyInference
{
    private bool Triggers(object node, object member, List<object> callees)
    {
        foreach(var trigger in TypeInitializer.TriggeredBy(node))
        {
            if(trigger.Type is ConcreteType or EnumType
                && TypeInitializer.Of(trigger.Type) is var initializer
                && !ReferenceEquals(initializer, member) && !initializer.NotRunBy(trigger, ScopeTypeOf(member)))
            {
                callees.Add(initializer);
            }
        }

        return node switch
        {
            InterpolatedStringExpression interpolated => interpolated.Parts.OfType<InterpolatedStringHole>().Any(hole => FormatWrites(hole.Expression, callees)),
            BinaryExpression { Operator: null, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate } concatenation
                when PurityJudge.DefinitionOf(concatenation.Type) is ConcreteType { IsIntrinsic: true, Name: "String" } =>
                FormatWrites(concatenation.Left, callees) || FormatWrites(concatenation.Right, callees),
            _ => false,
        };
    }

    private bool FormatWrites(Expression formatted, List<object> callees)
    {
        return ClrCallbacks.Implements(formatted.Type, "System.IFormattable") && Unproven(ClrCallbacks.Answering(formatted.Type, "ToString", 2), callees);
    }

    private static bool CallsFunctionValue(InvocationExpression call, Origins origins, List<object> callees)
    {
        if(call.Receiver.Type is IFunctionType { IsPure: true })
        {
            return false;
        }

        if(call.Receiver is LocalReferenceExpression { Local: var holder } && origins.LambdaHeldBy(holder) is { } lambda)
        {
            callees.Add(lambda);
            return false;
        }

        return true;
    }

    private static MistType? ScopeTypeOf(object member)
    {
        return member switch
        {
            Method method => method.DeclaringType,
            ConcreteType type => type,
            TypeInitializer initializer => initializer.Type,
            _ => null,
        };
    }
}