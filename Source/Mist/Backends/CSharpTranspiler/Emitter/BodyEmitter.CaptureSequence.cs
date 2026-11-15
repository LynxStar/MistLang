using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    private enum Reading
    {
        Value,
        Receiver,
        Reference,
        Target,
    }

    private readonly record struct Ahead(Expression? Part, SlotCapture? Capture, bool InPlace = false);

    private sealed class Sequence
    {
        public List<Ahead> Steps { get; } = [];

        public List<Expression> Owners { get; } = [];

        public Dictionary<Expression, List<SlotCapture>> Trailing { get; } = new(ReferenceEqualityComparer.Instance);

        public bool IsEmpty => Steps.Count == 0 && Trailing.Count == 0;

        public bool Fresh { get; init; }
    }

    private static Sequence Leading(Expression expression)
    {
        var sequence = new Sequence();
        Lead(expression, sequence);
        var last = sequence.Steps.FindLastIndex(step => step.Capture is not null);
        sequence.Steps.RemoveRange(last + 1, sequence.Steps.Count - last - 1);
        sequence.Owners.Add(expression);
        Expression? returned = null;

        foreach(var step in sequence.Steps.ToList())
        {
            if(step.InPlace)
            {
                returned = step.Part;
                _ = sequence.Steps.Remove(step);
            }
            else if(step.Capture is { } capture && returned is not null)
            {
                _ = sequence.Steps.Remove(step);

                if(!sequence.Trailing.TryGetValue(returned, out var trailing))
                {
                    sequence.Trailing[returned] = trailing = [];
                }

                trailing.Add(capture);
            }
        }

        return sequence;
    }

    private static void Lead(Expression expression, Sequence sequence)
    {
        if(UnitHoisting.Substitution(expression) is not null)
        {
            return;
        }

        switch(expression)
        {
            case InvocationExpression invocation:
                if(invocation.Receiver is MemberAccessExpression { Receiver: var receiver and not TypeReferenceExpression })
                {
                    Place(receiver, Held(receiver), sequence);
                }

                Own(invocation, sequence);
                PlaceArguments(invocation.Arguments, invocation.Parameters, sequence);
                break;

            case NewExpression creation:
                Own(creation, sequence);
                PlaceArguments(creation.Arguments, creation.Constructor?.ParametersList, sequence);
                break;

            case DefaultExpression or ArrayCreationExpression or StackallocExpression:
                Own(expression, sequence);
                break;

            case MemberAccessExpression access:
                if(access.Receiver is not TypeReferenceExpression)
                {
                    Place(access.Receiver, Held(access.Receiver), sequence);
                }

                Own(access, sequence);
                break;

            case AssignmentExpression assignment:
                Place(assignment.Target, Reading.Target, sequence);
                break;

            case CompoundAssignmentExpression compound:
                Place(compound.Target, Reading.Target, sequence);
                break;
        }
    }

    private static void Own(Expression owner, Sequence sequence)
    {
        var pending = sequenced?.Contains(owner) == true ? [] : Unconsumed(owner).Where(Evaluates).ToList();

        if(pending.Count > 0)
        {
            sequence.Steps.AddRange(pending.Select(capture => new Ahead(null, capture)));
            sequence.Owners.Add(owner);
        }
    }

    private static void PlaceArguments(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, Sequence sequence)
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            var byReference = parameters is not null && i < parameters.Count && ArgumentKeywords.For(parameters[i]).Length > 0;
            Place(arguments[i], byReference ? Reading.Reference : Reading.Value, sequence);
        }
    }

    private static Reading Held(Expression receiver)
    {
        var type = TypeNameEmitter.Closed(receiver.Type);

        return type is TypeParameter || TypeNameEmitter.IsValueKind(type) ? Reading.Receiver : Reading.Value;
    }

    private static void Place(Expression operand, Reading reading, Sequence sequence)
    {
        switch(operand)
        {
            case var _ when reading is Reading.Value:
                sequence.Steps.Add(new Ahead(operand, null));
                break;

            case InvocationExpression { Signature.Modifiers: { IsRef: true } returned }:
                Lead(operand, sequence);

                if(!returned.ReturnsReadonlyRef)
                {
                    sequence.Steps.Add(new Ahead(operand, null, InPlace: true));
                }

                break;

            case MemberAccessExpression { Receiver: TypeReferenceExpression }:
            case MemberAccessExpression { Member: FieldResult }:
            case MemberAccessExpression when reading is Reading.Target:
                Lead(operand, sequence);
                break;

            case IndexExpression index:
                Place(index.Receiver, Held(index.Receiver), sequence);

                if(index.Index is not LiteralExpression)
                {
                    Place(index.Index, Reading.Value, sequence);
                }

                break;

            case not FieldReferenceExpression when reading is Reading.Receiver:
                sequence.Steps.Add(new Ahead(operand, null));
                break;
        }
    }

    private static bool Binds(Expression part)
    {
        return !IsPlainRead(part)
            && !UnitHoisting.IsStable(part)
            && UnitHoisting.Substitution(part) is null
            && part is not (ArrayLiteralExpression or FunctionReferenceExpression)
            && boundParts?.ContainsKey(part) != true;
    }

    private static IEnumerable<Expression> BoundAhead(Expression operand, Reading reading)
    {
        var sequence = new Sequence();
        Place(operand, reading, sequence);

        return sequence.Steps.Where(step => !step.InPlace).Select(step => step.Part).OfType<Expression>().Where(part => !IsPlainRead(part) && UnitHoisting.Substitution(part) is null && boundParts?.ContainsKey(part) != true);
    }

    private static bool RunsInPlace(Expression receiver)
    {
        return Held(receiver) is Reading.Receiver && receiver switch
        {
            InvocationExpression { Signature.Modifiers.IsRef: true } => true,
            MemberAccessExpression { Member: FieldResult } access => RunsInPlace(access.Receiver),
            IndexExpression index => RunsInPlace(index.Receiver),
            _ => false,
        };
    }
}