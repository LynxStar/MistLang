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

        return FormattingCallbacks.Sites(node).Any(formatted => FormatWrites(formatting.Reach(formatted.Type), callees))
            || FormattingCallbacks.Uses(node, ScopeTypeOf(member)).Any(use => FormatWrites(formatting.Through(use), callees));
    }

    private bool FormatWrites(IEnumerable<FormatStep> steps, List<object> callees)
    {
        return steps.Any(step => step.Open is null && Unproven(step.Formatter, callees));
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