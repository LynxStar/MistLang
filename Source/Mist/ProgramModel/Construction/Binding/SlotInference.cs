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
/// <c>Len(a)</c> over an <c>int[]</c> binds <c>a.Length</c>. Through an abstract the read has no single
/// answer, so nothing binds and the call is told to state the parameter. Kept per candidate, since
/// inference runs for each and only the selected one's captures are owned.
/// </summary>
internal sealed class SlotInference(TypeInterner interner) : ISlotInference
{
    private readonly Dictionary<IOverload, List<SlotCapture>> captures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IOverload, (TypeParameter Parameter, IMistType Type)> refused = new(ReferenceEqualityComparer.Instance);

    public IMistType? Unknown(IOverload candidate, TypeParameter parameter, Expression argument, ConstructedType closing, int ordinal)
    {
        if(closing.Definition is not IHasTypeParameters declaring
            || declaring.TypeParametersList.Find(slot => slot.Ordinal == ordinal) is not { IsDependent: true, Domain: not null } slot)
        {
            return null;
        }

        if(closing.Definition is not (ClassType or StructType))
        {
            _ = refused.TryAdd(candidate, (parameter, argument.Type));
            return null;
        }

        var read = new MemberAccessExpression(argument.Node, argument, slot.Name, closing.TypeArguments[ordinal], member: new TypeResult(slot));
        var written = $"{SlotValueBinder.Written(argument.Node, "argument")}.{slot.Name}";

        if(SlotValueBinder.Capture(read, parameter, written, interner) is not { } capture)
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
}