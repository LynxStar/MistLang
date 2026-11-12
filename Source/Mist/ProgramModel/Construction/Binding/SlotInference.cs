using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal interface ISlotInference
{
    IMistType? Unknown(IOverload candidate, TypeParameter parameter, Expression argument, ConstructedType closing, int ordinal);
}

/// <summary>
/// What a method's dependent parameter binds from an argument whose type leaves the slot <c>unknown</c>
/// (runtime-dependent-values Requirements 8.3 and 11.5). Through a class or a struct the slot has a value
/// the object carries, so the parameter binds a capture on the invocation whose initializer reads it:
/// <c>Len(a)</c> over an <c>int[]</c> binds <c>a.Length</c>, a read typed by the slot's contract as the
/// capture it initializes is. Through an abstract the read has no single answer, so nothing binds and
/// the call is told to state the parameter. Kept per candidate, since inference runs for each and only
/// the selected one's captures are owned. The capture is named from the argument as written, which a
/// bound postfix argument no longer spans: its node is the last suffix.
/// </summary>
internal sealed class SlotInference(TypeInterner interner, IReadOnlyList<ExpressionNode> written) : ISlotInference
{
    private readonly Dictionary<IOverload, List<SlotCapture>> captures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IOverload, (TypeParameter Parameter, IMistType Type)> refused = new(ReferenceEqualityComparer.Instance);

    public SlotInference(TypeInterner interner)
        : this(interner, [])
    {
    }

    public IMistType? Unknown(IOverload candidate, TypeParameter parameter, Expression argument, ConstructedType closing, int ordinal)
    {
        if(closing.Definition is not IHasTypeParameters declaring
            || declaring.TypeParametersList.Find(slot => slot.Ordinal == ordinal) is not { IsDependent: true } slot
            || slot.Ordinal >= closing.TypeArguments.Count
            || InstanceSlotRead.Contract(slot) is not { } type)
        {
            return null;
        }

        if(closing.Definition is not (ClassType or StructType))
        {
            _ = refused.TryAdd(candidate, (parameter, argument.Type));
            return null;
        }

        var read = new MemberAccessExpression(argument.Node, argument, slot.Name, type, member: new TypeResult(slot));
        var spelled = $"{SlotValueBinder.Written(Spanning(argument), "argument")}.{slot.Name}";

        if(SlotValueBinder.Capture(read, parameter, spelled, interner) is not { } capture)
        {
            return null;
        }

        if(!captures.TryGetValue(candidate, out var owned))
        {
            owned = [];
            captures[candidate] = owned;
        }

        owned.Add(capture);
        return capture.Value;
    }

    public IReadOnlyList<SlotCapture> CapturesOf(IOverload candidate)
    {
        return captures.TryGetValue(candidate, out var owned) ? owned : [];
    }

    public (TypeParameter Parameter, IMistType Type)? RefusedFor(IOverload candidate)
    {
        return refused.TryGetValue(candidate, out var failure) ? failure : null;
    }

    private Storm.Runtime.SyntaxNode Spanning(Expression argument)
    {
        var span = argument.Node.Span;

        return written.FirstOrDefault(node => node.Span.Start <= span.Start && span.End <= node.Span.End) ?? argument.Node;
    }
}