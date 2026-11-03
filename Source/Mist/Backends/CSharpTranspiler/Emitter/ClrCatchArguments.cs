using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class ClrCatchArguments
{
    internal sealed record Prepared(Expression Value, IMistType Type, int Width, bool Spread);

    public static IReadOnlyList<Prepared> Prepare(InvocationExpression call)
    {
        var prepared = new List<Prepared>();
        for(var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            if(i > 0 && argument is SpreadExpression spread)
            {
                var tuple = (ITupleType)TypeNameEmitter.Closed(spread.Operand.Type);
                prepared.Add(new Prepared(spread.Operand, tuple, tuple.ElementTypes.Count, true));
                continue;
            }

            if(i > 0 && argument is MemberAccessExpression { MemberName: "Item1", Receiver: var receiver }
                && TypeNameEmitter.Closed(receiver.Type) is ITupleType elements
                && elements.ElementTypes.Count > 0
                && i + elements.ElementTypes.Count <= call.Arguments.Count
                && Enumerable.Range(0, elements.ElementTypes.Count).All(k => call.Arguments[i + k] is MemberAccessExpression access
                    && access.MemberName == $"Item{k + 1}" && ReferenceEquals(access.Receiver, receiver)))
            {
                prepared.Add(new Prepared(receiver, receiver.Type, elements.ElementTypes.Count, true));
                i += elements.ElementTypes.Count - 1;
                continue;
            }

            prepared.Add(new Prepared(argument, argument.Type, 1, false));
        }

        return prepared;
    }
}